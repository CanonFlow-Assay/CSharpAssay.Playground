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
        Assert.Equal(RejectionReason.QuoteExpiredBeforeDebit, Assert.IsType<RejectionEvidence>(boundary.Evidence.Value).Reason);
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
    public void TimeoutReconcilesEachPendingOperationUsingItsOriginalKey()
    {
        AssertLookup(new AwaitingDebit(Funds.Price, "debit-key"), new ReconcileDebit("debit-key"), PartnerOperation.Debit, "debit-key");
        AssertLookup(new AwaitingEscrow(Funds.Price, "debit-ref", "escrow-key"), new ReconcileEscrow("escrow-key"), PartnerOperation.EscrowHold, "escrow-key");
        AssertLookup(new Settling(Funds, "credit-ref", "settle-key"), new ReconcileEscrowRelease("settle-key"), PartnerOperation.EscrowRelease, "settle-key");
        AssertLookup(new Unwinding(Funds, "nonpayment-ref", "unwind-key"), new ReconcileEscrowRelease("unwind-key"), PartnerOperation.EscrowRelease, "unwind-key");
        AssertLookup(new RefundPending(new EscrowReleasedAfterNonpayment(Funds, "nonpayment-ref", "unwind-key"), "refund-key"),
            new ReconcileRefund("refund-key"), PartnerOperation.Refund, "refund-key");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreditThenCorrelatedNonpaymentDisputesBeforeOrAfterSettlement(bool settled)
    {
        SagaState state = settled ? new Settled(Funds, "credit-ref") : new Settling(Funds, "credit-ref", "release-key");
        var unrelated = PaymentSaga.Apply(state, new CreditRejected("foreign-key", "rejection-ref"), Now);
        Assert.Equal(state, unrelated.State);
        Assert.IsType<NoAction>(unrelated.Action.Value);
        var result = PaymentSaga.Apply(state, new CreditRejected(Funds.PayoutKey, "rejection-ref"), Now);
        var dispute = Assert.IsType<Disputed>(result.State.Value);
        Assert.IsType<Review>(result.Action.Value);
        Assert.Equal(new CreditConflict(Funds.PayoutKey, "credit-ref", "rejection-ref"), dispute.Conflict);
        Assert.Equal(dispute.Conflict, Assert.IsType<CreditConflict>(result.Evidence.Value));
        Assert.IsType<NoAction>(PaymentSaga.Apply(result.State, new EscrowReleased("release-key"), Now).Action.Value);
    }

    [Fact]
    public void DefinitiveDebitRejectionStopsWithoutEscrowOrRefund()
    {
        var state = new AwaitingDebit(Funds.Price, "debit-key");
        var unrelated = PaymentSaga.Apply(state, new DebitRejected("foreign-key", "not-booked"), Now);
        Assert.IsType<AwaitingDebit>(unrelated.State.Value);
        var result = PaymentSaga.Apply(state, new DebitRejected("debit-key", "not-booked"), Now);
        Assert.IsType<NoAction>(result.Action.Value);
        Assert.Equal(new RejectionEvidence(RejectionReason.DebitRejected, "not-booked"),
            Assert.IsType<RejectionEvidence>(result.Evidence.Value));
    }

    [Fact]
    public void DefinitiveEscrowRejectionPermitsRefundWithoutCreatingAPayout()
    {
        var pending = new AwaitingEscrow(Funds.Price, Funds.DebitReference, "escrow-key");
        var unrelated = PaymentSaga.Apply(pending, new EscrowRejected("foreign-key", "no-hold"), Now);
        Assert.IsType<NoAction>(unrelated.Action.Value);
        var result = PaymentSaga.Apply(pending, new EscrowRejected("escrow-key", "no-hold"), Now);
        var refund = Assert.IsType<Refund>(result.Action.Value);
        Assert.Equal(Funds.DebitReference, refund.DebitReference);
        Assert.Equal(Funds.Price.Transfer.Usd, refund.Usd);
        var state = Assert.IsType<RefundPending>(result.State.Value);
        Assert.Equal("no-hold", Assert.IsType<EscrowNeverHeld>(state.Basis.Value).RejectionReference);
        Assert.IsType<NoAction>(PaymentSaga.Apply(result.State, new EscrowRejected("escrow-key", "no-hold"), Now).Action.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefinitiveEscrowReleaseFailureFreezesBothReleasePaths(bool acceptCredit)
    {
        SagaState state = acceptCredit ? new Settling(Funds, "credit-ref", "release-key") : new Unwinding(Funds, "nonpayment-ref", "release-key");
        var frozen = PaymentSaga.Apply(state, new EscrowReleaseFailed("release-key", "release-failed"), Now);
        AssertFrozen(frozen, new OperationFailure(PartnerOperation.EscrowRelease, "release-key", "release-failed"));
        Assert.IsType<NoAction>(PaymentSaga.Apply(frozen.State, new EscrowReleased("release-key"), Now).Action.Value);
        PaymentEvent contradiction = acceptCredit ? new CreditRejected(Funds.PayoutKey, "no-credit") : new CreditConfirmed(Funds.PayoutKey, "late-credit");
        Assert.IsType<Disputed>(PaymentSaga.Apply(frozen.State, contradiction, Now).State.Value);
    }

    [Fact]
    public void DefinitiveRefundRejectionRequestsReviewWithoutReissuingRefund()
    {
        var pending = new RefundPending(new EscrowReleasedAfterNonpayment(Funds, "nonpayment-ref", "release-key"), "refund-key");
        var result = PaymentSaga.Apply(pending, new RefundRejected("refund-key", "refund-rejected"), Now);
        AssertFrozen(result, new OperationFailure(PartnerOperation.Refund, "refund-key", "refund-rejected"));
        Assert.IsType<NoAction>(PaymentSaga.Apply(result.State, new DebitRefunded("refund-key", "late-refund"), Now).Action.Value);
    }

    [Fact]
    public void SanctionsClearanceMustMatchCaseAndRecheckQuoteExpiry()
    {
        var hold = new SanctionsHold(Funds.Price, "screen-key", "case-ref");
        var wrongCase = PaymentSaga.Apply(hold, new SanctionsCleared("screen-key", "other-case", "review-ref"), Now);
        Assert.IsType<SanctionsHold>(wrongCase.State.Value);
        Assert.IsType<NoAction>(wrongCase.Action.Value);
        var cleared = new SanctionsCleared("screen-key", "case-ref", "review-ref");
        var expired = PaymentSaga.Apply(hold, cleared, ValidQuote.ExpiresAt);
        Assert.IsType<NoAction>(expired.Action.Value);
        Assert.Equal(RejectionReason.QuoteExpiredBeforeDebit, Assert.IsType<Rejected>(expired.State.Value).Reason);
        var valid = PaymentSaga.Apply(hold, cleared, ValidQuote.ExpiresAt.AddTicks(-1));
        Assert.IsType<Debit>(valid.Action.Value);
        Assert.IsType<NoAction>(PaymentSaga.Apply(valid.State, cleared, Now).Action.Value);
    }

    [Fact]
    public void SanctionsDenialRejectsAndEscalationKeepsTheHold()
    {
        var hold = new SanctionsHold(Funds.Price, "screen-key", "case-ref");
        var escalation = PaymentSaga.Apply(hold, new SanctionsEscalated("screen-key", "case-ref", "escalation-ref"), Now);
        Assert.IsType<SanctionsHold>(escalation.State.Value);
        Assert.IsType<Review>(escalation.Action.Value);
        Assert.Equal(SanctionsStatus.SanctionsEscalated, Assert.IsType<SanctionsEvidence>(escalation.Evidence.Value).Status);
        var denial = PaymentSaga.Apply(escalation.State, new SanctionsDenied("screen-key", "case-ref", "denial-ref"), Now);
        Assert.Equal(RejectionReason.SanctionsDenied, Assert.IsType<Rejected>(denial.State.Value).Reason);
        Assert.IsType<NoAction>(denial.Action.Value);
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

    private static void AssertLookup(SagaState initial, PaymentAction action, PartnerOperation operation, string key)
    {
        var lookup = PaymentSaga.Apply(initial, new TimedOut(), Now);
        Assert.Equal(initial, lookup.State);
        Assert.Equal(action, lookup.Action);
        Assert.Equal(new ReconciliationEvidence(operation, key), Assert.IsType<ReconciliationEvidence>(lookup.Evidence.Value));
        Assert.Equal(action, PaymentSaga.Apply(lookup.State, new TimedOut(), Now.AddSeconds(1)).Action);
    }

    private static void AssertFrozen(Decision decision, OperationFailure failure)
    {
        Assert.Equal(failure, Assert.IsType<PartnerReview>(decision.State.Value).Failure);
        Assert.Equal(failure, Assert.IsType<OperationFailure>(decision.Evidence.Value));
        Assert.Equal(failure.Reference, Assert.IsType<Review>(decision.Action.Value).Reference);
        Assert.IsType<NoAction>(PaymentSaga.Apply(decision.State, new TimedOut(), Now).Action.Value);
    }
}
