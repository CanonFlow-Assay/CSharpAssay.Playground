using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ExpenseGuard.Application;
using ExpenseGuard.Domain;

namespace ExpenseGuard.Infrastructure;

public sealed class PinnedRateSource(ImmutableArray<ExchangeRate> rates) : IRateSource
{
    public RateEvidence Get(string from, string to, DateOnly date) =>
        rates.SingleOrDefault(r => r.From == from && r.To == to && r.On == date) is ExchangeRate rate
            ? rate : new RateMissing();
}

public sealed class DemoAdvisor(string recommendation = "Approved") : IExpenseAdvisor
{
    private readonly string recommendation = recommendation is "Approved" or "Rejected" ? recommendation :
        throw new ArgumentException("The offline demonstration supports Approved or Rejected only.", nameof(recommendation));
    public Task<Advice> SuggestAsync(ExpenseClaim claim, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new Advice("offline-demo-v1", recommendation, claim.Category.ToString(),
            ["SIMULATED advice. Recommendation is a controlled display fixture, never decision authority."],
            "Advisory suggestion only. The deterministic engine evaluates the actual evidence.", true));
    }
}

public sealed class SimulatedPaymentGateway : IPaymentGateway
{
    public Task<string> PayAsync(ExpenseClaim claim, string idempotencyKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult("SIMULATED-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))[..24]);
    }
}

public static class ReceiptHashes
{
    public static string Compute(string previous, int sequence, string name, string actor, string role,
        DateTimeOffset at, string snapshot) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new { previous, sequence, name, actor, role, at = at.ToUniversalTime().ToString("O"), snapshot }))));
    public static bool Verify(ImmutableArray<AuditReceipt> receipts)
    {
        var previous = "GENESIS";
        var sequence = 0;
        foreach (var r in receipts)
        {
            if (r.Sequence != ++sequence || r.PreviousHash != previous || r.Hash != Compute(previous, r.Sequence, r.Event, r.Actor, r.Role, r.At, r.SnapshotJson)) return false;
            previous = r.Hash;
        }
        return receipts.Length > 0;
    }
}
