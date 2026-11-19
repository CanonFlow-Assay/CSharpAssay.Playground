using Playground.Saga.Demo;
using Playground.Saga.Domain;

namespace Playground.Saga.Tests;

public sealed class SagaTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Transfer Transfer = new("test-transfer", 100_000m);
    private static readonly Quote ValidQuote = new("quote-7", "USD", "EUR", 0.92m, Now.AddSeconds(20));
    private static readonly Funding Funds = new(new(Transfer, ValidQuote, 92_000m), "debit-ref", "escrow-ref", "test-transfer:payout");

    [Fact]
    public void TimeoutReconcilesBeforeLateCreditWithoutRefund()
    {
        var trace = TraceFor("late-success");
        Assert.Equal(nameof(Settled), trace.FinalState);
        var timeout = Assert.Single(trace.Entries, entry => entry.Observation.Event == nameof(TimedOut)).Observation;
        Assert.Equal(nameof(PayoutPending), timeout.State);
        Assert.Equal(nameof(ReconcileCredit), timeout.Action);
        Assert.Equal(trace.TransferId + ":payout", timeout.ActionData.GetProperty("PayoutKey").GetString());
        Assert.DoesNotContain(trace.Entries, entry => entry.Observation.Action == nameof(Refund));
        Assert.Single(trace.Entries, entry => entry.Observation.Action == nameof(SubmitCredit));
        Assert.Single(trace.Entries, entry => entry.Observation.Action == nameof(ReleaseEscrow));
        Assert.Equal(nameof(NoAction), trace.Entries[^1].Observation.Action);
    }

    [Fact]
    public void ConfirmedNonpaymentAndEscrowReleaseRequireOneRefund()
    {
        var trace = TraceFor("confirmed-nonpayment");
        Assert.Equal(nameof(Refunded), trace.FinalState);
        var rejected = Assert.Single(trace.Entries, entry => entry.Observation.Evidence == "DefinitiveNonpayment");
        Assert.Equal(nameof(ReleaseEscrow), rejected.Observation.Action);
        var refund = Assert.Single(trace.Entries, entry => entry.Observation.Action == nameof(Refund));
        Assert.Equal(nameof(EscrowReleased), refund.Observation.Event);
        Assert.True(refund.Sequence > rejected.Sequence);
        Assert.Equal(trace.TransferId + ":refund", refund.Observation.ActionData.GetProperty("Key").GetString());
        Assert.Equal("debit-100", refund.Observation.ActionData.GetProperty("DebitReference").GetString());
        Assert.Equal(100_000m, refund.Observation.ActionData.GetProperty("Usd").GetDecimal());
        Assert.Equal(nameof(NoAction), trace.Entries[^1].Observation.Action);
    }

    [Fact]
    public void MissingFxStopsWithoutFallbackRate()
    {
        var trace = TraceFor("missing-fx");
        Assert.Equal(nameof(Rejected), trace.FinalState);
        Assert.Equal("QuoteUnavailable", trace.Entries[^1].Observation.Evidence);
        Assert.Equal(nameof(NoAction), trace.Entries[^1].Observation.Action);
    }

    [Fact]
    public void SanctionsHoldKeepsItsReviewPathAfterTimeout()
    {
        var trace = TraceFor("sanctions-hold");
        Assert.Equal(nameof(SanctionsHold), trace.FinalState);
        Assert.DoesNotContain(trace.Entries, entry => entry.Observation.Action == nameof(Debit));
        var timeout = trace.Entries[^1].Observation;
        Assert.Equal(nameof(Review), timeout.Action);
        Assert.Equal("review-100", timeout.ActionData.GetProperty("Reference").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void InvalidTransferAmountNeverRequestsQuote(int usd)
    {
        var decision = PaymentSaga.Start(Transfer with { Usd = usd });
        Assert.IsType<Rejected>(decision.State.Value);
        Assert.IsType<NoAction>(decision.Action.Value);
    }

    [Fact]
    public void QuoteExpiryIsInclusiveAndRecheckedBeforeDebit()
    {
        var start = PaymentSaga.Start(Transfer);
        var response = new QuoteReceived(Transfer.Id, ValidQuote);
        var expired = PaymentSaga.Apply(start.State, response, ValidQuote.ExpiresAt);
        Assert.IsType<Rejected>(expired.State.Value);
        Assert.IsType<NoAction>(expired.Action.Value);

        var pending = PaymentSaga.Apply(start.State, response, Now);
        var screening = Assert.IsType<Screen>(pending.Action.Value);
        Assert.Equal(92_000m, screening.Price.Eur);
        var cleared = new ScreeningCleared(screening.Key, "cleared-ref");
        var boundary = PaymentSaga.Apply(pending.State, cleared, ValidQuote.ExpiresAt);
        Assert.IsType<NoAction>(boundary.Action.Value);
        Assert.Equal("QuoteExpiredBeforeDebit", boundary.Evidence);
        Assert.IsType<Debit>(PaymentSaga.Apply(pending.State, cleared, ValidQuote.ExpiresAt.AddTicks(-1)).Action.Value);
    }

    [Theory]
    [InlineData("EUR", "USD", 1)]
    [InlineData("USD", "EUR", 0)]
    [InlineData("USD", "EUR", -1)]
    [InlineData("USD", "EUR", 101)]
    public void InvalidRatesAndCurrencyPairsStopBeforeDebit(string from, string to, int rate)
    {
        var response = new QuoteReceived(Transfer.Id, ValidQuote with { From = from, To = to, Rate = rate });
        var result = PaymentSaga.Apply(PaymentSaga.Start(Transfer).State, response, Now);
        Assert.IsType<Rejected>(result.State.Value);
        Assert.IsType<NoAction>(result.Action.Value);
    }

    [Fact]
    public void CrossTransferAndOutOfOrderRepliesCannotAdvanceState()
    {
        var opened = PaymentSaga.Start(Transfer);
        var otherQuote = PaymentSaga.Apply(opened.State, new QuoteReceived("other-transfer", ValidQuote), Now);
        Assert.Equal(opened.State, otherQuote.State);
        Assert.IsType<NoAction>(otherQuote.Action.Value);

        var pending = new PayoutPending(Funds);
        var otherCredit = PaymentSaga.Apply(pending, new CreditConfirmed("other-transfer:payout", "credit-ref"), Now);
        Assert.IsType<PayoutPending>(otherCredit.State.Value);
        Assert.IsType<NoAction>(otherCredit.Action.Value);
        var outOfOrderRefund = PaymentSaga.Apply(pending, new DebitRefunded("test-transfer:refund", "out-of-order"), Now);
        Assert.IsType<PayoutPending>(outOfOrderRefund.State.Value);
        Assert.IsType<NoAction>(outOfOrderRefund.Action.Value);
    }

    [Fact]
    public void RepeatedTimeoutsReconcileTheOriginalPayoutKey()
    {
        var first = PaymentSaga.Apply(new PayoutPending(Funds), new TimedOut(), Now);
        var unknown = PaymentSaga.Apply(first.State, new CreditStillUnknown(Funds.PayoutKey), Now.AddSeconds(1));
        var repeat = PaymentSaga.Apply(unknown.State, new TimedOut(), Now.AddSeconds(2));
        Assert.IsType<NoAction>(unknown.Action.Value);
        Assert.Equal(Funds.PayoutKey, Assert.IsType<ReconcileCredit>(first.Action.Value).PayoutKey);
        Assert.Equal(first.Action, repeat.Action);
        Assert.Equal(first.State, repeat.State);
    }

    [Fact]
    public void ContradictoryCreditFreezesUnwindBeforeRefund()
    {
        var rejection = PaymentSaga.Apply(new PayoutPending(Funds), new CreditRejected(Funds.PayoutKey, "reject-ref"), Now);
        var release = Assert.IsType<ReleaseEscrow>(rejection.Action.Value);
        var conflict = PaymentSaga.Apply(rejection.State, new CreditConfirmed(Funds.PayoutKey, "credit-ref"), Now.AddSeconds(1));
        Assert.IsType<Review>(conflict.Action.Value);
        var released = PaymentSaga.Apply(conflict.State, new EscrowReleased(release.Key), Now.AddSeconds(2));
        Assert.IsType<Disputed>(released.State.Value);
        Assert.IsType<NoAction>(released.Action.Value);
    }

    [Fact]
    public void TimeoutsBeforeDebitOrEscrowConfirmationAvoidFundsMovement()
    {
        Assert.IsType<NoAction>(PaymentSaga.Apply(new AwaitingDebit(Funds.Price, "debit-key"), new TimedOut(), Now).Action.Value);
        Assert.IsType<NoAction>(PaymentSaga.Apply(new AwaitingEscrow(Funds.Price, "debit-ref", "escrow-key"), new TimedOut(), Now).Action.Value);
    }

    [Fact]
    public void JournalDetectsChangesRemovalAndReorderingAgainstOriginalHead()
    {
        var trace = TraceFor("late-success");
        var entries = trace.Entries;
        Assert.True(EvidenceJournal.Verify(entries, trace.HeadSha256));

        var first = entries[0];
        var modified = first with { Observation = first.Observation with { Evidence = "Changed" } };
        Assert.False(EvidenceJournal.Verify(entries.SetItem(0, modified), trace.HeadSha256));
        Assert.False(EvidenceJournal.Verify(entries.RemoveAt(entries.Length - 1), trace.HeadSha256));
        Assert.False(EvidenceJournal.Verify(entries.SetItem(0, entries[1]).SetItem(1, first), trace.HeadSha256));
    }

    private static Trace TraceFor(string scenario) => Assert.Single(Scenarios.Run(), trace => trace.Scenario == scenario);
}
