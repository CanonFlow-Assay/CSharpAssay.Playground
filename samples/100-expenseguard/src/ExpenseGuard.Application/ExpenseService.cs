using ExpenseGuard.Domain;

namespace ExpenseGuard.Application;

public sealed class ExpenseService(IExpenseStore store, IRateSource rates, TimeProvider time)
{
    public Task<StoreResult> CreateAsync(ExpenseClaim claim, Actor actor, CancellationToken ct) =>
        store.ExecuteAsync(claim.Id, actor, ledger =>
        {
            if (ledger.Claim is not null) return Mutation.Deny("ClaimExists");
            if (actor.Role != Role.Employee || actor.Id != claim.Employee || claim.Id.Value == Guid.Empty ||
                claim.State is not Draft || claim.Policy is not NoAssessment || claim.Receipt is not MissingReceipt ||
                string.IsNullOrWhiteSpace(claim.Employee.Value)) return Mutation.Deny("InvalidDraft");
            return new([new(claim, "DraftCreated")], false, "");
        }, ct);

    public Task<StoreResult> ExecuteAsync(ExpenseId id, int expectedVersion, Command command, Actor actor, CancellationToken ct) =>
        store.ExecuteAsync(id, actor, ledger =>
        {
            if (ledger.Claim is not ExpenseClaim claim || !Access.CanRead(claim, actor)) return Mutation.Deny("NotFound");
            if (ledger.Version != expectedVersion) return Mutation.Deny("VersionConflict");
            // Callers cannot inject policy, FX, historical totals or duplicate evidence.
            if (command is Check or Apply or Reimburse) return Mutation.Deny("InternalCommand");
            return Transitions.Decide(claim, command, actor) switch
            {
                Accepted result => new([new(result.Claim, result.Event)], false, ""),
                Denied denied => Mutation.Deny(denied.Code)
            };
        }, ct);

    public Task<StoreResult> AssessAsync(ExpenseId id, int expectedVersion, Actor actor, CancellationToken ct) =>
        store.ExecuteAsync(id, actor, ledger =>
        {
            if (ledger.Claim is not ExpenseClaim c || c.Employee != actor.Id || actor.Role != Role.Employee)
                return Mutation.Deny("NotFound");
            if (ledger.Version != expectedVersion) return Mutation.Deny("VersionConflict");
            var others = ledger.Claims.Where(x => x.Id != c.Id && x.State is not (Draft or Rejected)).ToArray();
            var duplicate = others.Any(x =>
                (c.Receipt is Receipt r && x.Receipt is Receipt other &&
                    string.Equals(r.Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(x.Merchant.Trim(), c.Merchant.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    x.Amount == c.Amount && x.SpentOn == c.SpentOn));
            var meals = others.Where(x => x.Employee == c.Employee && x.SpentOn == c.SpentOn && x.Category == Category.Meal)
                .Select(x => x.Policy is Assessment a ? a.BaseAmount : 0m).Sum();
            var facts = new PolicyFacts(DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime),
                rates.Get(c.Amount.Currency, Policies.Demo.BaseCurrency, c.SpentOn), duplicate, meals);
            return Transitions.Decide(c, new Check(Policies.Demo, facts), actor) switch
            {
                Denied d => Mutation.Deny(d.Code),
                Accepted checkedClaim => Transitions.Decide(checkedClaim.Claim, new Apply(), actor) switch
                {
                    Denied d => Mutation.Deny(d.Code),
                    Accepted applied => new([new(checkedClaim.Claim, checkedClaim.Event), new(applied.Claim, applied.Event)], false, "")
                }
            };
        }, ct);

    public Task<StoreResult> RequestPaymentAsync(ExpenseId id, int expectedVersion, Actor actor, CancellationToken ct) =>
        store.ExecuteAsync(id, actor, ledger =>
        {
            if (ledger.Claim is not ExpenseClaim c || !Access.CanRead(c, actor)) return Mutation.Deny("NotFound");
            if (ledger.Version != expectedVersion) return Mutation.Deny("VersionConflict");
            if (actor.Role != Role.Finance || c.State is not Approved || c.Policy is not Assessment)
                return Mutation.Deny("PaymentRequiresFinanceAndApproval");
            return new([new(c, "ReimbursementRequested")], true, "");
        }, ct);

    public Task<StoreResult> RecordExtractionRequestAsync(ExpenseId id, int expectedVersion, Actor actor, CancellationToken ct) =>
        store.ExecuteAsync(id, actor, ledger =>
        {
            if (ledger.Claim is not ExpenseClaim c || c.Employee != actor.Id || actor.Role != Role.Employee) return Mutation.Deny("NotFound");
            if (ledger.Version != expectedVersion) return Mutation.Deny("VersionConflict");
            if (c.Receipt is not Receipt) return Mutation.Deny("StoredReceiptRequired");
            return new([new(c, "ReceiptExtractionRequested")], false, "");
        }, ct);
}
