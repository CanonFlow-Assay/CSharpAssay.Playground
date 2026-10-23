using Assay.Workflow.Domain;
using PollyAssay.Adapter;
using PollyAssay.Domain;
using Xunit;

namespace Assay.Workflow.Tests;

public sealed class PollyTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("retry-recovery")]
    [InlineData("permanent")]
    [InlineData("exhausted")]
    [InlineData("deadline")]
    [InlineData("total-deadline")]
    [InlineData("cancel-before")]
    [InlineData("cancel-during")]
    public async Task RealPollyPipelinesMatchIndependentStatusAttemptAndTraceRequirements(string id)
    {
        var scenario = Scenarios.Registered.Single(s => s.Id == id);
        var original = await Scenarios.RunAsync(scenario, false, TestContext.Current.CancellationToken);
        var functional = await Scenarios.RunAsync(scenario, true, TestContext.Current.CancellationToken);
        Assert.Equal(scenario.ExpectedStatus, functional.Response.Status);
        Assert.Equal(scenario.ExpectedAttempts, functional.Attempts);
        Assert.Equal(original.Response, functional.Response);
        Assert.Equal(original.Attempts, functional.Attempts);
        Assert.Empty(ResilienceAssay.Check(functional.Trace, new(3, TimeSpan.FromSeconds(2).Ticks, false, true)));
        if (id is "success" or "retry-recovery") { Assert.Equal("document", functional.Response.Content); }
    }

    [Fact]
    public async Task SharedBreakerRecoveryConcurrencyAndFallbackHaveExecutableChecks()
    {
        var checks = await Scenarios.InfrastructureChecksAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, checks.Length);
        Assert.Contains("breaker-recovered", checks);
        Assert.Contains("concurrency-limit-rejects", checks);
        Assert.Contains("fallback-marked-degraded", checks);
    }

    [Theory]
    [InlineData(ReplyKind.Success, false)]
    [InlineData(ReplyKind.Transient, true)]
    [InlineData(ReplyKind.Permanent, false)]
    public void DomainClassificationIsExplicit(ReplyKind kind, bool retry) => Assert.Equal(retry, FetchDecisions.ShouldRetry(new(kind, "")));

    [Fact]
    public void RuntimeContractProjectionComesFromPublicResponseAndActualOutcomeMapping()
    {
        var snapshot = Scenarios.ObserveContract();
        Assert.Equal(2, snapshot.Members.Length);
        Assert.Equal(7, snapshot.Outcomes.Length);
        Assert.Contains(new ContractMember("Status", "System.String", true), snapshot.Members);
        Assert.Contains("degraded", snapshot.Outcomes);
    }
}
