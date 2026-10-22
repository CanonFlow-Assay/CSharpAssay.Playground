using System.Collections.Immutable;

namespace Assay.Workflow.Domain;

public enum SupportStatus { Unknown, Native, Unsupported, Incomplete }
public enum ExecutionState { NotRun, Passed, Failed, Invalid, TimedOut, Cancelled, ToolFailure, Killed, Survived }
public enum WorkflowStatus { Incomplete, ReadyForReview, Failed, ToolFailure }
public sealed record Requirement(string Id, string Description, ImmutableArray<string> ProbeIds);
public sealed record EvidenceReference(string Path, string Sha256);
public sealed record ModuleEvidence(string Module, ExecutionState State, bool Required, bool Authoritative,
    string SnapshotSha256, string RequirementsSha256, ImmutableArray<EvidenceReference> Artifacts, ImmutableArray<string> Observations);
public sealed record Assessment(string Schema, string SnapshotSha256, string RequirementsSha256,
    SupportStatus Support, ExecutionState Baseline, ImmutableArray<Requirement> Requirements, ImmutableArray<ModuleEvidence> Modules);
public sealed record RegisteredProbe(string Id, string RequirementId, string Module);

public static class AssessmentRules
{
    public const string Schema = "assay-application-assessment/0.1";

    public static bool HasCompleteIdentity(Assessment assessment) => assessment.Schema == Schema &&
        IsSha256(assessment.SnapshotSha256) && IsSha256(assessment.RequirementsSha256) &&
        !assessment.Modules.IsDefault && !assessment.Requirements.IsDefaultOrEmpty &&
        assessment.Requirements.All(r => !string.IsNullOrWhiteSpace(r.Id) && !string.IsNullOrWhiteSpace(r.Description) && !r.ProbeIds.IsDefault) &&
        assessment.Requirements.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() == assessment.Requirements.Length &&
        assessment.Modules.Select(m => m.Module).Distinct(StringComparer.Ordinal).Count() == assessment.Modules.Length &&
        assessment.Modules.All(m => m.SnapshotSha256 == assessment.SnapshotSha256 && m.RequirementsSha256 == assessment.RequirementsSha256 &&
            !m.Artifacts.IsDefault && m.Artifacts.All(a => IsSha256(a.Sha256) && !string.IsNullOrWhiteSpace(a.Path)) && !m.Observations.IsDefault);

    public static WorkflowStatus Evaluate(Assessment assessment)
    {
        if (!HasCompleteIdentity(assessment)) { return WorkflowStatus.Incomplete; }
        if (assessment.Modules.Any(m => m.State == ExecutionState.ToolFailure) || assessment.Baseline == ExecutionState.ToolFailure) { return WorkflowStatus.ToolFailure; }
        if (assessment.Baseline == ExecutionState.Failed || assessment.Modules.Any(m => m.State == ExecutionState.Failed)) { return WorkflowStatus.Failed; }
        if (assessment.Modules.Any(m => m.State is ExecutionState.NotRun or ExecutionState.Invalid or ExecutionState.TimedOut or ExecutionState.Cancelled)) { return WorkflowStatus.Incomplete; }
        if (assessment.Support != SupportStatus.Native || assessment.Baseline != ExecutionState.Passed ||
            !assessment.Modules.Any(m => m.Module == "CSharpAssay" && m.Required && m.Authoritative && m.State == ExecutionState.Passed) ||
            assessment.Modules.Any(m => m.Required && (m.State != ExecutionState.Passed || m.Artifacts.IsEmpty))) { return WorkflowStatus.Incomplete; }
        return WorkflowStatus.ReadyForReview;
    }

    public static bool IsSha256(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
}
