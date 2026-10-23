using System.Collections.Immutable;
using Assay.Workflow.Domain;

namespace Playground.DecisionAssist.Domain;

public sealed record ProbeRequested(string ProbeId, string RequirementId, string Reason);
public sealed record ContextRequired(string Reason);
public sealed record SurvivorReview(string Reason);
public readonly record struct NoInvestigation;
public readonly record struct BudgetExhausted;
public readonly union ApplicationDecision(ProbeRequested, ContextRequired, SurvivorReview, NoInvestigation, BudgetExhausted);

public static class ApplicationAssistance
{
    public static ApplicationDecision Decide(Assessment assessment, ImmutableArray<RegisteredProbe> registry, int remainingProbes)
    {
        if (!AssessmentRules.HasCompleteIdentity(assessment) || assessment.Support != SupportStatus.Native) { return new ContextRequired("Obtain complete source-bound evidence and supported project scope."); }
        if (assessment.Baseline != ExecutionState.Passed) { return new ContextRequired("Resolve the original baseline before investigation or inference."); }
        if (!assessment.Modules.Any(m => m.Module == "CSharpAssay" && m.Required && m.Authoritative && m.State is ExecutionState.Passed or ExecutionState.Failed) || assessment.Modules.Any(m => m.Required && m.Artifacts.IsEmpty)) { return new ContextRequired("Required authoritative engine or execution artifacts are missing."); }
        if (assessment.Modules.Any(m => m.State is ExecutionState.Invalid or ExecutionState.NotRun or ExecutionState.TimedOut or ExecutionState.Cancelled or ExecutionState.ToolFailure)) { return new ContextRequired("Incomplete/invalid execution requires local evidence repair before inference."); }
        if (remainingProbes <= 0) { return new BudgetExhausted(); }
        if (registry.IsDefault || registry.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != registry.Length || registry.Any(p => !assessment.Requirements.Any(r => r.Id == p.RequirementId && r.ProbeIds.Contains(p.Id)))) { return new ContextRequired("The probe registry does not match declared requirements."); }
        foreach (var module in assessment.Modules.Where(m => m.State == ExecutionState.Failed).OrderBy(m => m.Module, StringComparer.Ordinal))
        {
            var probes = registry.Where(p => p.Module == module.Module).OrderBy(p => p.Id, StringComparer.Ordinal).ToImmutableArray();
            if (!probes.IsEmpty) { return new ProbeRequested(probes[0].Id, probes[0].RequirementId, "Investigate the observed failure in " + module.Module); }
            return new ContextRequired("Register a requirement-bound probe for " + module.Module);
        }
        return assessment.Modules.Any(m => m.State == ExecutionState.Survived)
            ? new SurvivorReview("Retain every survivor; inspect assertions, reachability and contract intent. Equivalence is not automatic.")
            : new NoInvestigation();
    }

    public static ApplicationDecision Select(Assessment assessment, ImmutableArray<RegisteredProbe> registry, int remainingProbes, string proposedProbeId, string proposalSnapshot)
    {
        var baseline = Decide(assessment, registry, remainingProbes);
        if (baseline is not ProbeRequested || proposalSnapshot != assessment.SnapshotSha256) { return baseline; }
        var candidates = registry.Where(p => p.Id == proposedProbeId && assessment.Modules.Any(m => m.Module == p.Module && m.State == ExecutionState.Failed)).ToImmutableArray();
        return candidates.Length == 1 ? new ProbeRequested(candidates[0].Id, candidates[0].RequirementId, "Validated advisory selection; execution requires orchestration.") : baseline;
    }
}
