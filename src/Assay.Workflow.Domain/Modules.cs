using System.Collections.Immutable;

namespace Assay.Workflow.Domain;

public sealed record ContractMember(string Id, string Type, bool Required);
public sealed record ContractSnapshot(ImmutableArray<ContractMember> Members, ImmutableArray<string> Outcomes);
public sealed record ContractDifference(string Id, string Reason);
public sealed record PreviewCheck<T>(ExecutionState State, ImmutableArray<T> Issues);

public static class ContractAssay
{
    public static ImmutableArray<ContractDifference> Compare(ContractSnapshot baseline, ContractSnapshot candidate) => Evaluate(baseline, candidate).Issues;

    public static PreviewCheck<ContractDifference> Evaluate(ContractSnapshot baseline, ContractSnapshot candidate)
    {
        var issues = ImmutableArray.CreateBuilder<ContractDifference>();
        if (!IsComplete(baseline) || !IsComplete(candidate))
        {
            return new(ExecutionState.Invalid, [new("contract", "Missing, blank or duplicate contract identities/types/outcomes; comparison is incomplete.")]);
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
        return new(issues.Count == 0 ? ExecutionState.Passed : ExecutionState.Failed,
            issues.OrderBy(i => i.Id, StringComparer.Ordinal).ThenBy(i => i.Reason, StringComparer.Ordinal).ToImmutableArray());
    }

    private static bool IsComplete(ContractSnapshot snapshot) => snapshot is not null &&
        !snapshot.Members.IsDefault && !snapshot.Outcomes.IsDefault && !(snapshot.Members.IsEmpty && snapshot.Outcomes.IsEmpty) &&
        snapshot.Members.All(m => m is not null && !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Type)) &&
        snapshot.Members.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() == snapshot.Members.Length &&
        snapshot.Outcomes.All(o => !string.IsNullOrWhiteSpace(o)) &&
        snapshot.Outcomes.Distinct(StringComparer.Ordinal).Count() == snapshot.Outcomes.Length;
}

public sealed record RiskObservation(string Id, bool Changed, bool CoverageKnown, decimal LineCoverage,
    int Complexity, int SurvivingMutants);
public sealed record RiskPriority(string Id, int Ordinal, ImmutableArray<string> Reasons);
public sealed record RiskEvaluation(ExecutionState State, ImmutableArray<RiskPriority> Priorities, ImmutableArray<string> Issues);

public static class CodeRiskAssay
{
    public static RiskEvaluation Evaluate(ImmutableArray<RiskObservation> observations)
    {
        if (observations.IsDefaultOrEmpty || observations.Any(o => o is null || string.IsNullOrWhiteSpace(o.Id) ||
            o.Complexity < 0 || o.SurvivingMutants < 0 || (o.CoverageKnown && (o.LineCoverage < 0m || o.LineCoverage > 100m))) ||
            observations.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != observations.Length)
        {
            return new(ExecutionState.Invalid, [], ["Missing, duplicate or invalid risk observations; ranking is incomplete."]);
        }
        return new(ExecutionState.Passed, Rank(observations), []);
    }

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
    public static ImmutableArray<string> Check(ImmutableArray<OperationTrace> trace, TraceContract contract) => Evaluate(trace, contract).Issues;

    public static PreviewCheck<string> Evaluate(ImmutableArray<OperationTrace> trace, TraceContract contract)
    {
        if (trace.IsDefaultOrEmpty || contract is null || contract.MaximumAttempts < 1 || contract.DeadlineTicks <= 0 ||
            trace.Any(r => r is null || string.IsNullOrWhiteSpace(r.Operation) || !Enum.IsDefined(r.Kind) || r.Attempt < 0))
        {
            return new(ExecutionState.Invalid, ["Missing trace or invalid declared trace identity/kind/contract."]);
        }
        var issues = ImmutableArray.CreateBuilder<string>();
        var incomplete = false;
        foreach (var operation in trace.GroupBy(item => item.Operation).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var rows = operation.ToImmutableArray();
            if (rows.Where((r, i) => r.Sequence != i + 1 || r.ElapsedTicks < 0 || (i > 0 && r.ElapsedTicks < rows[i - 1].ElapsedTicks)).Any())
            {
                issues.Add(operation.Key + ": non-monotonic trace."); incomplete = true;
            }
            var currentAttempt = 0;
            foreach (var row in rows)
            {
                var valid = row.Kind == TraceKind.Attempt ? row.Attempt == ++currentAttempt
                    : row.Kind == TraceKind.SideEffect ? row.Attempt > 0 && row.Attempt <= currentAttempt
                    : row.Attempt == currentAttempt && (currentAttempt > 0 || row.Kind is TraceKind.Cancelled or TraceKind.Rejected or TraceKind.Timeout);
                if (!valid) { issues.Add(operation.Key + ": event has no matching observed attempt."); incomplete = true; }
            }
            if (rows[^1].Kind is TraceKind.Attempt or TraceKind.SideEffect)
            {
                issues.Add(operation.Key + ": missing final outcome."); incomplete = true;
            }
            if (rows.Count(r => r.Kind == TraceKind.Attempt) > contract.MaximumAttempts) { issues.Add(operation.Key + ": attempt budget exceeded."); }
            if (rows.Max(r => r.ElapsedTicks) > contract.DeadlineTicks) { issues.Add(operation.Key + ": total deadline exceeded."); }
            var cancellation = rows.Where(r => r.Kind == TraceKind.Cancelled).Select(r => r.Sequence).DefaultIfEmpty(int.MaxValue).Min();
            if (rows.Any(r => r.Sequence > cancellation)) { issues.Add(operation.Key + ": work continued after cancellation."); }
            if (!contract.AllowsFallback && rows.Any(r => r.Kind == TraceKind.Fallback)) { issues.Add(operation.Key + ": undeclared fallback."); }
            if (contract.AtMostOneSideEffect && rows.Count(r => r.Kind == TraceKind.SideEffect) > 1) { issues.Add(operation.Key + ": duplicated side effect."); }
            var permanent = rows.Where(r => r.Kind == TraceKind.PermanentFailure).Select(r => r.Sequence).DefaultIfEmpty(int.MaxValue).Min();
            if (rows.Any(r => r.Sequence > permanent && r.Kind == TraceKind.Attempt)) { issues.Add(operation.Key + ": permanent failure retried."); }
            var success = rows.Where(r => r.Kind == TraceKind.Success).Select(r => r.Sequence).DefaultIfEmpty(int.MaxValue).Min();
            if (rows.Any(r => r.Sequence > success)) { issues.Add(operation.Key + ": work continued after success."); }
        }
        return new(incomplete ? ExecutionState.Invalid : issues.Count > 0 ? ExecutionState.Failed : ExecutionState.Passed, issues.ToImmutable());
    }
}
