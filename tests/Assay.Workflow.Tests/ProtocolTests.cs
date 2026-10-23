using System.Collections.Immutable;
using Assay.Workflow.Domain;
using Playground.DecisionAssist.Domain;
using Xunit;

namespace Assay.Workflow.Tests;

public sealed class ProtocolTests
{
    private static readonly string Snapshot = new('a', 64);
    private static readonly string Requirements = new('b', 64);
    private static readonly ImmutableArray<RegisteredProbe> Registry = [new("contract", "requirement", "ContractAssay")];
    private static Assessment Valid() => new(AssessmentRules.Schema, Snapshot, Requirements, SupportStatus.Native, ExecutionState.Passed,
        [new("requirement", "Pinned requirement", ["contract"])],
        [new("CSharpAssay", ExecutionState.Passed, true, true, Snapshot, Requirements, [new("receipt.json", new('c', 64))], [])]);

    [Fact]
    public void CompleteNativeEvidenceIsReadyForReviewRatherThanNewReleaseAuthority() => Assert.Equal(WorkflowStatus.ReadyForReview, AssessmentRules.Evaluate(Valid()));

    [Theory]
    [InlineData(SupportStatus.Unknown)]
    [InlineData(SupportStatus.Unsupported)]
    [InlineData(SupportStatus.Incomplete)]
    public void UnsupportedScopesCannotBeReady(SupportStatus support) => Assert.Equal(WorkflowStatus.Incomplete, AssessmentRules.Evaluate(Valid() with { Support = support }));

    [Theory]
    [InlineData(ExecutionState.NotRun)]
    [InlineData(ExecutionState.TimedOut)]
    [InlineData(ExecutionState.Cancelled)]
    [InlineData(ExecutionState.Invalid)]
    public void IncompleteBaselineNeverRequestsProbe(ExecutionState state)
    {
        var assessment = Valid() with { Baseline = state };
        Assert.Equal(WorkflowStatus.Incomplete, AssessmentRules.Evaluate(assessment));
        Assert.IsType<ContextRequired>(ApplicationAssistance.Decide(assessment, Registry, 5).Value);
    }

    [Fact]
    public void FailedBaselineAndToolFailuresStayExplicit()
    {
        Assert.Equal(WorkflowStatus.Failed, AssessmentRules.Evaluate(Valid() with { Baseline = ExecutionState.Failed }));
        Assert.Equal(WorkflowStatus.ToolFailure, AssessmentRules.Evaluate(Valid() with { Baseline = ExecutionState.ToolFailure }));
    }

    [Fact]
    public void MissingRequiredArtifactsCannotBeReady()
    {
        var original = Valid();
        var assessment = original with { Modules = [original.Modules[0] with { Artifacts = [] }] };
        Assert.Equal(WorkflowStatus.Incomplete, AssessmentRules.Evaluate(assessment));
    }

    [Fact]
    public void ProvisionalEngineEvidenceCannotBePromoted()
    {
        var original = Valid();
        Assert.Equal(WorkflowStatus.Incomplete, AssessmentRules.Evaluate(original with { Modules = [original.Modules[0] with { Authoritative = false }] }));
    }

    [Fact]
    public void StaleSourceAndRequirementBindingsAreRejected()
    {
        var original = Valid();
        foreach (var changed in new[] { original.Modules[0] with { SnapshotSha256 = new('d', 64) }, original.Modules[0] with { RequirementsSha256 = new('e', 64) } })
        {
            Assert.False(AssessmentRules.HasCompleteIdentity(original with { Modules = [changed] }));
        }
    }

    [Fact]
    public void DuplicateModulesAndRequirementsAreRejected()
    {
        var a = Valid();
        Assert.False(AssessmentRules.HasCompleteIdentity(a with { Modules = [a.Modules[0], a.Modules[0]] }));
        Assert.False(AssessmentRules.HasCompleteIdentity(a with { Requirements = [a.Requirements[0], a.Requirements[0]] }));
    }

    [Fact]
    public void FailedContractSelectsOnlyRegisteredRequirementBoundProbe()
    {
        var a = WithContractFailure();
        Assert.Equal("contract", Assert.IsType<ProbeRequested>(ApplicationAssistance.Decide(a, Registry, 1).Value).ProbeId);
        Assert.Equal("contract", Assert.IsType<ProbeRequested>(ApplicationAssistance.Select(a, Registry, 1, "run-shell-command", Snapshot).Value).ProbeId);
        Assert.IsType<BudgetExhausted>(ApplicationAssistance.Decide(a, Registry, 0).Value);
    }

    [Fact]
    public void UnknownOrDuplicateRegistryRequiresContext()
    {
        Assert.IsType<ContextRequired>(ApplicationAssistance.Decide(WithContractFailure(), [new("undeclared", "requirement", "ContractAssay")], 1).Value);
        Assert.IsType<ContextRequired>(ApplicationAssistance.Decide(WithContractFailure(), [Registry[0], Registry[0]], 1).Value);
    }

    [Fact]
    public void InvalidAndIncompleteModulesNeverBecomeInferenceCandidates()
    {
        var a = WithContractFailure();
        Assert.IsType<ContextRequired>(ApplicationAssistance.Decide(a with { Modules = [.. a.Modules, a.Modules[1] with { Module = "MutationAssay", State = ExecutionState.Invalid }] }, Registry, 1).Value);
        var ready = Valid();
        foreach (var state in new[] { ExecutionState.Invalid, ExecutionState.NotRun, ExecutionState.TimedOut, ExecutionState.Cancelled, ExecutionState.ToolFailure, ExecutionState.Failed })
        {
            var incomplete = ready with { Modules = [.. ready.Modules, ready.Modules[0] with { Module = "MutationAssay", Required = false, State = state }] };
            Assert.NotEqual(WorkflowStatus.ReadyForReview, AssessmentRules.Evaluate(incomplete));
        }
    }

    [Fact]
    public void SurvivorsRemainReviewCases()
    {
        var a = Valid();
        var survivor = new ModuleEvidence("MutationAssay", ExecutionState.Survived, false, false, Snapshot, Requirements, [], ["equivalence unresolved"]);
        Assert.IsType<SurvivorReview>(ApplicationAssistance.Decide(a with { Modules = [.. a.Modules, survivor] }, Registry, 1).Value);
    }

    private static Assessment WithContractFailure()
    {
        var a = Valid();
        return a with { Modules = [.. a.Modules, new("ContractAssay", ExecutionState.Failed, true, false, Snapshot, Requirements, [new("contract.json", new('f', 64))], ["Removed member"])] };
    }
}
