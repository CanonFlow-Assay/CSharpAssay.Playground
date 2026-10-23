using Assay.Workflow.Domain;
using Xunit;

namespace Assay.Workflow.Tests;

public sealed class PreviewEvidenceTests
{
    [Theory]
    [InlineData(TraceKind.Fallback)]
    [InlineData(TraceKind.TransientFailure)]
    [InlineData(TraceKind.Timeout)]
    public void CancellationCannotBeReplacedByAFallbackOrAnotherOutcome(TraceKind afterCancellation)
    {
        var result = ResilienceAssay.Evaluate([
            new("op", 1, 1, 0, TraceKind.Attempt),
            new("op", 2, 1, 1, TraceKind.Cancelled),
            new("op", 3, 1, 2, afterCancellation)
        ], new(3, 100, true, true));
        Assert.Equal(ExecutionState.Failed, result.State);
        Assert.Contains("op: work continued after cancellation.", result.Issues);
    }

    [Fact]
    public void InvalidContractEvidenceIsDistinctFromAnObservedBreak()
    {
        ContractSnapshot baseline = new([new("status", "System.String", true)], ["ok"]);
        Assert.Equal(ExecutionState.Passed, ContractAssay.Evaluate(baseline, baseline).State);
        Assert.Equal(ExecutionState.Invalid, ContractAssay.Evaluate(baseline, baseline with { Outcomes = ["ok", "ok"] }).State);
        Assert.Equal(ExecutionState.Failed, ContractAssay.Evaluate(baseline, baseline with { Members = [] }).State);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("blank")]
    [InlineData("negative-complexity")]
    [InlineData("negative-mutants")]
    [InlineData("coverage-over-100")]
    [InlineData("negative-coverage")]
    public void InvalidRiskObservationsNeverBecomeACompletedRanking(string shape)
    {
        var row = new RiskObservation("file.cs", true, true, 80m, 1, 0);
        System.Collections.Immutable.ImmutableArray<RiskObservation> observations = shape switch
        {
            "empty" => [],
            "duplicate" => [row, row],
            "blank" => [row with { Id = " " }],
            "negative-complexity" => [row with { Complexity = -1 }],
            "negative-mutants" => [row with { SurvivingMutants = -1 }],
            "coverage-over-100" => [row with { LineCoverage = 101m }],
            _ => [row with { LineCoverage = -1m }]
        };
        var result = CodeRiskAssay.Evaluate(observations);
        Assert.Equal(ExecutionState.Invalid, result.State);
        Assert.Empty(result.Priorities);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void UnknownCoverageRetainsItsReasonRatherThanInventingAMeasurement()
    {
        var result = CodeRiskAssay.Evaluate([new("file.cs", false, false, 0m, 0, 0)]);
        Assert.Equal(ExecutionState.Passed, result.State);
        Assert.Equal("coverage-unknown", Assert.Single(Assert.Single(result.Priorities).Reasons));
    }

    [Fact]
    public void TraceCompletenessAndPolicyViolationHaveDifferentStates()
    {
        var contract = new TraceContract(1, 100, false, true);
        OperationTrace attempt = new("op", 1, 1, 0, TraceKind.Attempt);
        OperationTrace success = new("op", 2, 1, 1, TraceKind.Success);
        Assert.Equal(ExecutionState.Invalid, ResilienceAssay.Evaluate([attempt], contract).State);
        Assert.Equal(ExecutionState.Failed, ResilienceAssay.Evaluate([attempt, success with { ElapsedTicks = 101 }], contract).State);
        Assert.Equal(ExecutionState.Passed, ResilienceAssay.Evaluate([attempt, success], contract).State);
        Assert.Equal(ExecutionState.Passed, ResilienceAssay.Evaluate([new("op", 1, 0, 0, TraceKind.Cancelled)], contract).State);
    }

    [Theory]
    [InlineData("blank-id")]
    [InlineData("blank-type")]
    [InlineData("blank-outcome")]
    [InlineData("duplicate-outcome")]
    public void MalformedContractEvidenceCannotLookCompatible(string shape)
    {
        ContractSnapshot snapshot = shape switch
        {
            "blank-id" => new([new("", "System.String", true)], ["ok"]),
            "blank-type" => new([new("name", "", true)], ["ok"]),
            "blank-outcome" => new([new("name", "System.String", true)], [""]),
            _ => new([new("name", "System.String", true)], ["ok", "ok"])
        };
        Assert.NotEmpty(ContractAssay.Compare(snapshot, snapshot));
    }

    [Theory]
    [InlineData("orphan-success")]
    [InlineData("unfinished")]
    [InlineData("unknown-kind")]
    [InlineData("blank-operation")]
    [InlineData("attempt-gap")]
    [InlineData("unobserved-attempt")]
    public void IncompleteOrContradictoryTraceCannotLookCompliant(string shape)
    {
        var attempt = new OperationTrace("op", 1, 1, 0, TraceKind.Attempt);
        var success = new OperationTrace("op", 2, 1, 1, TraceKind.Success);
        System.Collections.Immutable.ImmutableArray<OperationTrace> trace = shape switch
        {
            "orphan-success" => [success with { Sequence = 1 }],
            "unfinished" => [attempt],
            "unknown-kind" => [attempt, success with { Kind = (TraceKind)999 }],
            "blank-operation" => [attempt with { Operation = "" }, success with { Operation = "" }],
            "attempt-gap" => [attempt with { Attempt = 2 }, success with { Attempt = 2 }],
            _ => [attempt, success with { Attempt = 2 }]
        };
        Assert.NotEmpty(ResilienceAssay.Check(trace, new(3, 100, false, true)));
    }
}
