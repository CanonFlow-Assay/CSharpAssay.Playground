using System.Collections.Immutable;
using ExpenseGuard.Domain;

namespace ExpenseGuard.Application;

public sealed record Ledger(ExpenseClaim? Claim, int Version, ImmutableArray<ExpenseClaim> Claims);
public sealed record Change(ExpenseClaim Claim, string Event);
public sealed record Mutation(ImmutableArray<Change> Changes, bool QueuePayment, string Error)
{
    public static Mutation Deny(string code) => new([], false, code);
}
public sealed record StoreResult(ExpenseClaim? Claim, int Version, string Error);
public sealed record AuditReceipt(int Sequence, string Event, string Actor, string Role, DateTimeOffset At,
    string SnapshotJson, string PreviousHash, string Hash);
public sealed record EvidenceBundle(ExpenseClaim Claim, int Version, ImmutableArray<AuditReceipt> Receipts, bool HashChainValid);
public sealed record ListedClaim(ExpenseClaim Claim, int Version, bool HashChainValid);
public interface IExpenseStore
{
    Task<StoreResult> ExecuteAsync(ExpenseId id, Actor actor, Func<Ledger, Mutation> decide, CancellationToken ct);
    Task<EvidenceBundle?> ReadAsync(ExpenseId id, CancellationToken ct);
    Task<ImmutableArray<ListedClaim>> ListAsync(Actor actor, CancellationToken ct);
}
public interface IRateSource { RateEvidence Get(string from, string to, DateOnly date); }
public sealed record Advice(string Provider, string ProposedState, string CategorySuggestion,
    ImmutableArray<string> Notes, string Summary, bool Simulated);
public interface IExpenseAdvisor { Task<Advice> SuggestAsync(ExpenseClaim claim, CancellationToken ct); }
public sealed record ReceiptDraft(string? Merchant, decimal? Amount, string? Currency, DateOnly? SpentOn, string? CategoryGuess, bool? Itemized);
public sealed record ExtractionProposal(string Status, string Provider, ReceiptDraft? Draft, string ProposedState, ImmutableArray<string> Notes, string Summary);
public interface IReceiptExtractor
{
    string Mode { get; }
    bool Enabled { get; }
    Task<ExtractionProposal> ExtractAsync(byte[] bytes, string contentType, CancellationToken ct);
}
public interface IPaymentGateway
{
    // Implementations must honor this stable key across retries, including commit failure after sending.
    Task<string> PayAsync(ExpenseClaim claim, string idempotencyKey, CancellationToken ct);
}

public static class Access
{
    public static bool CanRead(ExpenseClaim claim, Actor actor) => actor.Id == claim.Employee ||
        actor.Role is Role.Manager or Role.Finance or Role.Auditor;
}
