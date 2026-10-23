using System.Collections.Immutable;

namespace Assay.Workflow.Domain;

public sealed record ContractMember(string Id, string Type, bool Required);
public sealed record ContractSnapshot(ImmutableArray<ContractMember> Members, ImmutableArray<string> Outcomes);
public sealed record ContractDifference(string Id, string Reason);

public static class ContractAssay
{
    public static ImmutableArray<ContractDifference> Compare(ContractSnapshot baseline, ContractSnapshot candidate)
    {
        var issues = ImmutableArray.CreateBuilder<ContractDifference>();
        if (baseline.Members.IsDefault || candidate.Members.IsDefault || baseline.Outcomes.IsDefault || candidate.Outcomes.IsDefault ||
            baseline.Members.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != baseline.Members.Length ||
            candidate.Members.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != candidate.Members.Length)
        {
            return [new("contract", "Missing or duplicate contract members; comparison is incomplete.")];
        }
        foreach (var member in baseline.Members)
        {
            var matches = candidate.Members.Where(m => m.Id == member.Id).ToImmutableArray();
            if (matches.IsEmpty) { issues.Add(new(member.Id, "Pinned member was removed.")); }
            else if (matches[0].Type != member.Type || matches[0].Required != member.Required) { issues.Add(new(member.Id, "Pinned member type or requiredness changed.")); }
        }
        foreach (var member in candidate.Members.Where(m => m.Required && !baseline.Members.Any(b => b.Id == m.Id)))
        {
            issues.Add(new(member.Id, "New required member changes the pinned input/wire contract."));
        }
        foreach (var outcome in baseline.Outcomes.Except(candidate.Outcomes, StringComparer.Ordinal)) { issues.Add(new(outcome, "Pinned outcome was removed.")); }
        foreach (var outcome in candidate.Outcomes.Except(baseline.Outcomes, StringComparer.Ordinal)) { issues.Add(new(outcome, "New outcome requires explicit contract review.")); }
        return issues.OrderBy(i => i.Id, StringComparer.Ordinal).ThenBy(i => i.Reason, StringComparer.Ordinal).ToImmutableArray();
    }
}

public sealed record RiskObservation(string Id, bool Changed, bool CoverageKnown, decimal LineCoverage,
    int Complexity, int SurvivingMutants);
public sealed record RiskPriority(string Id, int Ordinal, ImmutableArray<string> Reasons);

public static class CodeRiskAssay
{
    // This is a declared ordinal heuristic, never a calibrated defect probability.
    public static ImmutableArray<RiskPriority> Rank(ImmutableArray<RiskObservation> observations) => observations.Select(item =>
    {
        var reasons = ImmutableArray.CreateBuilder<string>();
        var ordinal = 0;
        if (item.Changed) { ordinal += 2; reasons.Add("changed"); }
        if (!item.CoverageKnown) { ordinal += 1; reasons.Add("coverage-unknown"); }
        else if (item.LineCoverage < 80m) { ordinal += 3; reasons.Add("coverage-below-declared-80-percent-triage-boundary"); }
        if (item.Complexity >= 10) { ordinal += 2; reasons.Add("complexity-at-least-10"); }
        if (item.SurvivingMutants > 0) { ordinal += 4; reasons.Add("surviving-mutants"); }
        return new RiskPriority(item.Id, ordinal, reasons.ToImmutable());
    }).OrderByDescending(item => item.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray();
}

public enum TraceKind { Attempt, Success, TransientFailure, PermanentFailure, Cancelled, Timeout, Rejected, Fallback, SideEffect }
public sealed record OperationTrace(string Operation, int Sequence, int Attempt, long ElapsedTicks, TraceKind Kind);
public sealed record TraceContract(int MaximumAttempts, long DeadlineTicks, bool AllowsFallback, bool AtMostOneSideEffect);

public static class ResilienceAssay
{
    public static ImmutableArray<string> Check(ImmutableArray<OperationTrace> trace, TraceContract contract)
    {
        if (trace.IsDefaultOrEmpty || contract.MaximumAttempts < 1 || contract.DeadlineTicks <= 0) { return ["Missing trace or invalid declared trace contract."]; }
        var issues = ImmutableArray.CreateBuilder<string>();
        foreach (var operation in trace.GroupBy(item => item.Operation).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var rows = operation.ToImmutableArray();
            if (rows.Select(r => r.Sequence).Distinct().Count() != rows.Length || rows.Where((r, i) => r.Sequence != i + 1 || r.ElapsedTicks < 0 || (i > 0 && r.ElapsedTicks < rows[i - 1].ElapsedTicks)).Any()) { issues.Add(operation.Key + ": non-monotonic trace."); }
            if (rows.Count(r => r.Kind == TraceKind.Attempt) > contract.MaximumAttempts) { issues.Add(operation.Key + ": attempt budget exceeded."); }
            if (rows.Max(r => r.ElapsedTicks) > contract.DeadlineTicks) { issues.Add(operation.Key + ": total deadline exceeded."); }
            var cancellation = rows.Where(r => r.Kind == TraceKind.Cancelled).Select(r => r.Sequence).DefaultIfEmpty(int.MaxValue).Min();
            if (rows.Any(r => r.Sequence > cancellation && r.Kind is TraceKind.Attempt or TraceKind.SideEffect or TraceKind.Success)) { issues.Add(operation.Key + ": work continued after cancellation."); }
            if (!contract.AllowsFallback && rows.Any(r => r.Kind == TraceKind.Fallback)) { issues.Add(operation.Key + ": undeclared fallback."); }
            if (contract.AtMostOneSideEffect && rows.Count(r => r.Kind == TraceKind.SideEffect) > 1) { issues.Add(operation.Key + ": duplicated side effect."); }
            var permanent = rows.Where(r => r.Kind == TraceKind.PermanentFailure).Select(r => r.Sequence).DefaultIfEmpty(int.MaxValue).Min();
            if (rows.Any(r => r.Sequence > permanent && r.Kind == TraceKind.Attempt)) { issues.Add(operation.Key + ": permanent failure retried."); }
        }
        return issues.ToImmutable();
    }
}
