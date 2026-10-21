using Polly;
using Polly.Retry;
using Polly.Timeout;
using PollyAssay.Domain;

namespace PollyAssay.Adapter;

public sealed record FetchOptions(int MaximumAttempts, TimeSpan TotalDeadline);
public delegate ValueTask<TransportReply> FetchTransport(CancellationToken cancellationToken);

public sealed class FetchClient
{
    private readonly ResiliencePipeline<TransportReply> pipeline;

    public FetchClient(FetchOptions options, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumAttempts, 1);
        pipeline = new ResiliencePipelineBuilder<TransportReply> { TimeProvider = time }
            .AddTimeout(options.TotalDeadline)
            .AddRetry(new RetryStrategyOptions<TransportReply>
            {
                MaxRetryAttempts = options.MaximumAttempts - 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => new ValueTask<bool>(args.Outcome.Exception is null && args.Outcome.Result is { } reply && FetchDecisions.ShouldRetry(reply))
            }).Build();
    }

    public async Task<FetchResponse> FetchAsync(FetchTransport transport, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await pipeline.ExecuteAsync(token => transport(token), cancellationToken).ConfigureAwait(false);
            return FetchDecisions.Project(FetchDecisions.Convert(reply));
        }
        catch (TimeoutRejectedException) { return FetchDecisions.Project(new FetchFailed(FetchFailureKind.Deadline)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return FetchDecisions.Project(new FetchCancelled()); }
    }
}

// Ordinary consumer control: direct mapping, same declared transport behavior.
public sealed class OriginalFetchClient
{
    private readonly ResiliencePipeline<TransportReply> pipeline;
    public OriginalFetchClient(FetchOptions options, TimeProvider time)
    {
        pipeline = new ResiliencePipelineBuilder<TransportReply> { TimeProvider = time }
            .AddTimeout(options.TotalDeadline)
            .AddRetry(new RetryStrategyOptions<TransportReply>
            {
                MaxRetryAttempts = options.MaximumAttempts - 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => new ValueTask<bool>(args.Outcome.Exception is null && args.Outcome.Result is { Kind: ReplyKind.Transient })
            }).Build();
    }
    public async Task<FetchResponse> FetchAsync(FetchTransport transport, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await pipeline.ExecuteAsync(token => transport(token), cancellationToken).ConfigureAwait(false);
            return new(reply.Kind switch { ReplyKind.Success => "success", ReplyKind.Permanent => "permanent", ReplyKind.Transient => "exhausted", _ => throw new ArgumentOutOfRangeException(nameof(transport)) }, reply.Kind == ReplyKind.Success ? reply.Content : "");
        }
        catch (TimeoutRejectedException) { return new("timeout", ""); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new("cancelled", ""); }
    }
}
