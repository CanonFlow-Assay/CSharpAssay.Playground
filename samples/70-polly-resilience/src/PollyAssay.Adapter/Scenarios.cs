using System.Collections.Immutable;
using System.Reflection;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using Assay.Workflow.Domain;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.RateLimiting;
using PollyAssay.Domain;

namespace PollyAssay.Adapter;

public sealed record FaultStep(ReplyKind Kind, TimeSpan Delay, string Content);
public sealed record FetchScenario(string Id, ImmutableArray<FaultStep> Steps, bool CancelBefore, bool CancelDuring, string ExpectedStatus, int ExpectedAttempts);
public sealed record ScenarioResult(string Id, string Pipeline, FetchResponse Response, int Attempts, ImmutableArray<OperationTrace> Trace);

public static class Scenarios
{
    public static ImmutableArray<FetchScenario> Registered { get; } =
    [
        new("success", [new(ReplyKind.Success, TimeSpan.Zero, "document")], false, false, "success", 1),
        new("retry-recovery", [new(ReplyKind.Transient, TimeSpan.Zero, ""), new(ReplyKind.Transient, TimeSpan.Zero, ""), new(ReplyKind.Success, TimeSpan.Zero, "document")], false, false, "success", 3),
        new("permanent", [new(ReplyKind.Permanent, TimeSpan.Zero, "")], false, false, "permanent", 1),
        new("exhausted", [new(ReplyKind.Transient, TimeSpan.Zero, "")], false, false, "exhausted", 3),
        new("deadline", [new(ReplyKind.Success, TimeSpan.FromSeconds(5), "late")], false, false, "timeout", 1),
        new("total-deadline", [new(ReplyKind.Transient, TimeSpan.FromMilliseconds(750), "")], false, false, "timeout", 3),
        new("cancel-before", [new(ReplyKind.Success, TimeSpan.Zero, "")], true, false, "cancelled", 0),
        new("cancel-during", [new(ReplyKind.Success, TimeSpan.FromSeconds(5), "")], false, true, "cancelled", 1)
    ];

    public static async Task<ScenarioResult> RunAsync(FetchScenario scenario, bool functional, CancellationToken cancellationToken)
    {
        var time = new ControlledTime();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var events = new List<OperationTrace>();
        var started = Channel.CreateUnbounded<int>();
        var attempts = 0;
        void Observe(TraceKind kind) { events.Add(new(scenario.Id, events.Count + 1, attempts, time.GetTimestamp(), kind)); }
        async ValueTask<TransportReply> Transport(CancellationToken token)
        {
            var number = ++attempts;
            if (number > 12) { throw new InvalidOperationException("Controlled scenario exceeded its invocation budget."); }
            Observe(TraceKind.Attempt);
            var step = scenario.Steps[Math.Min(number - 1, scenario.Steps.Length - 1)];
            var delay = Task.Delay(step.Delay, time, token);
            started.Writer.TryWrite(number);
            await delay.ConfigureAwait(false);
            Observe(step.Kind switch { ReplyKind.Success => TraceKind.Success, ReplyKind.Transient => TraceKind.TransientFailure, _ => TraceKind.PermanentFailure });
            return new(step.Kind, step.Content);
        }
        if (scenario.CancelBefore) { caller.Cancel(); }
        var options = new FetchOptions(3, TimeSpan.FromSeconds(2));
        var operation = functional ? new FetchClient(options, time).FetchAsync(Transport, caller.Token)
            : new OriginalFetchClient(options, time).FetchAsync(Transport, caller.Token);
        for (var budget = 0; !operation.IsCompleted && budget < 12; budget++)
        {
            var next = started.Reader.ReadAsync(cancellationToken).AsTask();
            if (await Task.WhenAny(operation, next).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false) == operation) { break; }
            await next.ConfigureAwait(false);
            if (scenario.CancelDuring) { caller.Cancel(); }
            else if (!operation.IsCompleted && !time.AdvanceNext()) { throw new InvalidOperationException("No controlled progress is available."); }
        }
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        if (result.Status == "cancelled") { Observe(TraceKind.Cancelled); }
        if (result.Status == "timeout") { Observe(TraceKind.Timeout); }
        return new(scenario.Id, functional ? "functional" : "original", result, attempts, events.ToImmutableArray());
    }

    public static ContractSnapshot ObserveContract() => new(
        typeof(FetchResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => new ContractMember(p.Name, p.PropertyType.FullName ?? p.PropertyType.Name, true)).ToImmutableArray(),
        ImmutableArray.Create<FetchOutcome>(new Fetched(""), new FetchDegraded(""), new FetchCancelled(), new FetchFailed(FetchFailureKind.Permanent), new FetchFailed(FetchFailureKind.Exhausted), new FetchFailed(FetchFailureKind.Deadline), new FetchFailed(FetchFailureKind.Rejected))
            .Select(o => FetchDecisions.Project(o).Status).Order(StringComparer.Ordinal).ToImmutableArray());

    public static async Task<ImmutableArray<string>> InfrastructureChecksAsync(CancellationToken cancellationToken)
    {
        var passed = ImmutableArray.CreateBuilder<string>();
        var time = new ControlledTime();
        var state = new CircuitBreakerStateProvider();
        var breaker = new ResiliencePipelineBuilder<TransportReply> { TimeProvider = time }.AddCircuitBreaker(new CircuitBreakerStrategyOptions<TransportReply>
        {
            FailureRatio = 1,
            MinimumThroughput = 2,
            SamplingDuration = TimeSpan.FromSeconds(10),
            BreakDuration = TimeSpan.FromSeconds(5),
            StateProvider = state,
            ShouldHandle = args => new ValueTask<bool>(args.Outcome.Result is { Kind: ReplyKind.Transient })
        }).Build();
        for (var index = 0; index < 2; index++) { await breaker.ExecuteAsync(_ => new ValueTask<TransportReply>(new TransportReply(ReplyKind.Transient, "")), cancellationToken).ConfigureAwait(false); }
        if (state.CircuitState != CircuitState.Open) { throw new InvalidDataException("Breaker did not open."); }
        try { await breaker.ExecuteAsync(_ => new ValueTask<TransportReply>(new TransportReply(ReplyKind.Success, "unexpected")), cancellationToken).ConfigureAwait(false); throw new InvalidDataException("Open breaker admitted work."); }
        catch (BrokenCircuitException) { passed.Add("breaker-open-rejects"); }
        time.Advance(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<TransportReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = breaker.ExecuteAsync(async token => { entered.TrySetResult(); return await release.Task.WaitAsync(token).ConfigureAwait(false); }, cancellationToken).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        try { await breaker.ExecuteAsync(_ => new ValueTask<TransportReply>(new TransportReply(ReplyKind.Success, "unexpected")), cancellationToken).ConfigureAwait(false); throw new InvalidDataException("Concurrent recovery probe admitted."); }
        catch (BrokenCircuitException) { passed.Add("concurrent-recovery-rejected"); }
        release.SetResult(new(ReplyKind.Success, "recovered"));
        await probe.ConfigureAwait(false);
        if (state.CircuitState != CircuitState.Closed) { throw new InvalidDataException("Breaker did not recover."); }
        passed.Add("breaker-recovered");

        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 });
        var limited = new ResiliencePipelineBuilder().AddRateLimiter(limiter).Build();
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = limited.ExecuteAsync(async token => { admitted.TrySetResult(); await finish.Task.WaitAsync(token).ConfigureAwait(false); }, cancellationToken).AsTask();
        await admitted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        try { await limited.ExecuteAsync(_ => ValueTask.CompletedTask, cancellationToken).ConfigureAwait(false); throw new InvalidDataException("Concurrency limit admitted excess work."); }
        catch (RateLimiterRejectedException) { passed.Add("concurrency-limit-rejects"); }
        finish.SetResult();
        await first.ConfigureAwait(false);
        await limited.ExecuteAsync(_ => ValueTask.CompletedTask, cancellationToken).ConfigureAwait(false);
        passed.Add("concurrency-permit-released");

        var fallback = new ResiliencePipelineBuilder<TransportReply>().AddFallback(new FallbackStrategyOptions<TransportReply>
        {
            ShouldHandle = args => new ValueTask<bool>(args.Outcome.Result is { Kind: ReplyKind.Transient }),
            FallbackAction = _ => Outcome.FromResultAsValueTask(new TransportReply(ReplyKind.Success, "cached"))
        }).Build();
        var replacement = await fallback.ExecuteAsync(_ => new ValueTask<TransportReply>(new TransportReply(ReplyKind.Transient, "")), cancellationToken).ConfigureAwait(false);
        var response = FetchDecisions.Project(new FetchDegraded(replacement.Content));
        if (response != new FetchResponse("degraded", "cached")) { throw new InvalidDataException("Fallback was not distinguishable."); }
        passed.Add("fallback-marked-degraded");
        return passed.ToImmutable();
    }
}
