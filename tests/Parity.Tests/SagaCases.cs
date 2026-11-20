using System.Text.Json;
using Playground.Saga.Domain;

namespace Playground.Parity.Tests;

internal static class SagaCases
{
    private static readonly DateTimeOffset Started = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private const string TransferId = "parity-transfer";
    private static string Key(string operation) => $"{TransferId}:{operation}";

    internal static string Run(JsonElement steps)
    {
        var decision = PaymentSaga.Start(new(TransferId, 100m));
        var actions = new List<string> { DescribeAction(decision.Action) };
        var observedAt = Started;
        foreach (var step in steps.EnumerateArray())
        {
            var (message, time) = Parse(NativeCases.Text(step));
            observedAt = time > observedAt ? time : observedAt;
            decision = PaymentSaga.Apply(decision.State, message, observedAt);
            actions.Add(DescribeAction(decision.Action));
        }
        return string.Join(',', actions) + "|" + DescribeState(decision.State);
    }

    private static (PaymentEvent Message, DateTimeOffset Time) Parse(string step)
    {
        PaymentEvent message = step switch
        {
            "quote" => new QuoteReceived(TransferId, new("q1", "USD", "EUR", 0.9m, Started.AddSeconds(40))),
            "screen" => new ScreeningCleared(Key("screen"), "screened"),
            "hold" => new ScreeningHeld(Key("screen"), "sanctions-case"),
            "late-clear" => new SanctionsCleared(Key("screen"), "sanctions-case", "approved"),
            "debit" => new DebitPosted(Key("debit"), "debit-ref"),
            "escrow" => new EscrowHeld(Key("escrow"), "escrow-ref"),
            "no-escrow" => new EscrowRejected(Key("escrow"), "not-held"),
            "timeout" => new TimedOut(),
            "unknown" => new CreditStillUnknown(Key("payout")),
            "credit" => new CreditConfirmed(Key("payout"), "bank-ref"),
            "nonpayment" => new CreditRejected(Key("payout"), "definitive-no-credit"),
            "foreign-nonpayment" => new CreditRejected("unrelated:payout", "definitive-no-credit"),
            "release-settled" => new EscrowReleased(Key("release-settled")),
            "release-unwind" => new EscrowReleased(Key("release-unwind")),
            "release-failed" => new EscrowReleaseFailed(Key("release-unwind"), "release-rejected"),
            "refund-posted" => new DebitRefunded(Key("refund"), "refund-ref"),
            _ => throw new JsonException($"Unknown payment event {step}.")
        };
        var seconds = step switch { "timeout" => 30, "unknown" => 32, "credit" => 35, "late-clear" => 41, _ => 0 };
        return (message, Started.AddSeconds(seconds));
    }

    private static string DescribeAction(PaymentAction action) => action switch
    {
        NoAction => "none",
        RequestQuote request when request.TransferId == TransferId => "quote",
        Screen request when request.Key == Key("screen") => "screen",
        Debit request when request.Key == Key("debit") => "debit",
        HoldEscrow request when request.Key == Key("escrow") && request.DebitReference == "debit-ref" => "hold-escrow",
        SubmitCredit request when request.Key == Key("payout") && request.Funds.EscrowReference == "escrow-ref" => "submit-credit",
        ReconcileCredit request when request.PayoutKey == Key("payout") => "reconcile-credit",
        ReconcileDebit request when request.DebitKey == Key("debit") => "reconcile-debit",
        ReconcileEscrow request when request.EscrowKey == Key("escrow") => "reconcile-escrow",
        ReconcileEscrowRelease request when request.ReleaseKey == Key("release-unwind") => "reconcile-release",
        ReconcileRefund request when request.RefundKey == Key("refund") => "reconcile-refund",
        ReleaseEscrow request when request.Key == Key("release-settled") && request.EscrowReference == "escrow-ref" => "release-settled",
        ReleaseEscrow request when request.Key == Key("release-unwind") && request.EscrowReference == "escrow-ref" => "release-unwind",
        Refund request when request.Key == Key("refund") && request.DebitReference == "debit-ref" && request.Usd == 100m => "refund",
        Review => "review",
        _ => throw new InvalidOperationException($"Unexpected payment action {action}.")
    };

    private static string DescribeState(SagaState state) => state switch
    {
        AwaitingEscrow => "awaiting-escrow",
        PayoutPending => "pending",
        Settled => "settled",
        Refunded => "refunded",
        Rejected => "rejected",
        Disputed => "disputed",
        PartnerReview => "partner-review",
        _ => throw new InvalidOperationException($"Unexpected final payment state {state}.")
    };
}
