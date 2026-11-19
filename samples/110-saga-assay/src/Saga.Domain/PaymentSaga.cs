namespace Playground.Saga.Domain;

public static class PaymentSaga
{
    public static Decision Start(Transfer transfer) =>
        string.IsNullOrWhiteSpace(transfer.Id) || transfer.Usd is <= 0m or > 1_000_000m ||
        decimal.Round(transfer.Usd, 2, MidpointRounding.ToEven) != transfer.Usd
            ? Reject(RejectionReason.InvalidUsdAmountOrIdentity)
            : Progress(new AwaitingQuote(transfer), new RequestQuote(transfer.Id), ProgressKind.QuoteRequested);

    public static Decision Apply(SagaState state, PaymentEvent message, DateTimeOffset observedAt)
    {
        if (message is CreditConfirmed paid && HasReference(paid.Reference) &&
            PriorPayout(state) is RecordedNonpayment nonpayment && nonpayment.Key == paid.Key)
        {
            return Dispute(new CreditConflict(paid.Key, paid.Reference, nonpayment.Reference), paid.Reference);
        }

        if (message is CreditRejected rejected && HasReference(rejected.Reference) &&
            PriorPayout(state) is RecordedCredit credit && credit.Key == rejected.Key)
        {
            return Dispute(new CreditConflict(rejected.Key, credit.Reference, rejected.Reference), rejected.Reference);
        }

        return message is TimedOut ? OnTimeout(state) : state switch
        {
            AwaitingQuote awaiting => Price(awaiting, message, observedAt),
            AwaitingScreening awaiting => ScreenResult(awaiting, message, observedAt),
            AwaitingDebit awaiting => BookDebit(awaiting, message),
            AwaitingEscrow awaiting => BookEscrow(awaiting, message),
            PayoutPending awaiting => PayoutResult(awaiting, message),
            Settling awaiting => CompleteSettlement(awaiting, message),
            Unwinding awaiting => CompleteUnwind(awaiting, message),
            RefundPending awaiting => CompleteRefund(awaiting, message),
            SanctionsHold hold => ResolveSanctions(hold, message, observedAt),
            Settled or Refunded or Rejected or Disputed or PartnerReview => Ignore(state)
        };
    }

    private static Decision OnTimeout(SagaState state) => state switch
    {
        AwaitingQuote or AwaitingScreening => Reject(RejectionReason.TimedOutBeforeDebit),
        AwaitingDebit debit => Reconcile(state, new ReconcileDebit(debit.DebitKey), PartnerOperation.Debit, debit.DebitKey),
        AwaitingEscrow escrow => Reconcile(state, new ReconcileEscrow(escrow.EscrowKey), PartnerOperation.EscrowHold, escrow.EscrowKey),
        PayoutPending pending => Reconcile(state, new ReconcileCredit(pending.Funds.PayoutKey), PartnerOperation.Credit, pending.Funds.PayoutKey),
        Settling settling => ReconcileRelease(state, settling.ReleaseKey),
        Unwinding unwinding => ReconcileRelease(state, unwinding.ReleaseKey),
        RefundPending refund => Reconcile(state, new ReconcileRefund(refund.RefundKey), PartnerOperation.Refund, refund.RefundKey),
        Settled or Refunded or Rejected or Disputed or PartnerReview => Ignore(state),
        SanctionsHold hold => ReviewSanctions(hold, SanctionsStatus.SanctionsHoldNeedsReview, hold.CaseReference)
    };

    private static Decision Price(AwaitingQuote state, PaymentEvent message, DateTimeOffset observedAt)
    {
        if (message is QuoteUnavailable unavailable && unavailable.TransferId == state.Transfer.Id)
        {
            return Reject(RejectionReason.QuoteUnavailable);
        }

        if (message is not QuoteReceived received || received.TransferId != state.Transfer.Id)
        {
            return Ignore(state);
        }

        var quote = received.Quote;
        if (string.IsNullOrWhiteSpace(quote.Id) || quote.From != "USD" || quote.To != "EUR" ||
            quote.Rate is <= 0m or > 100m || quote.ExpiresAt <= observedAt)
        {
            return Reject(RejectionReason.QuoteInvalidOrExpired);
        }

        var eur = decimal.Round(state.Transfer.Usd * quote.Rate, 2, MidpointRounding.ToEven);
        if (eur <= 0m)
        {
            return Reject(RejectionReason.RoundedPayoutIsZero);
        }

        var price = new PricedTransfer(state.Transfer, quote, eur);
        return Progress(new AwaitingScreening(price), new Screen(Key(price, "screen"), price), ProgressKind.QuoteAccepted);
    }

    private static Decision ScreenResult(AwaitingScreening state, PaymentEvent message, DateTimeOffset observedAt)
    {
        var expected = Key(state.Price, "screen");
        if (message is ScreeningHeld held && held.Key == expected && HasReference(held.CaseReference))
        {
            var hold = new SanctionsHold(state.Price, expected, held.CaseReference);
            return ReviewSanctions(hold, SanctionsStatus.SanctionsHold, held.CaseReference);
        }

        return message is ScreeningCleared cleared && cleared.Key == expected && HasReference(cleared.Reference)
            ? AuthorizeDebit(state.Price, observedAt)
            : Ignore(state);
    }

    private static Decision ResolveSanctions(SanctionsHold hold, PaymentEvent message, DateTimeOffset observedAt)
    {
        bool SameCase(string key, string caseReference, string reference) =>
            key == hold.ScreenKey && caseReference == hold.CaseReference && HasReference(reference);

        if (message is SanctionsCleared cleared && SameCase(cleared.Key, cleared.CaseReference, cleared.DecisionReference))
        {
            return AuthorizeDebit(hold.Price, observedAt);
        }
        if (message is SanctionsDenied denied && SameCase(denied.Key, denied.CaseReference, denied.DecisionReference))
        {
            return Reject(RejectionReason.SanctionsDenied, denied.DecisionReference);
        }
        if (message is SanctionsEscalated escalated && SameCase(escalated.Key, escalated.CaseReference, escalated.DecisionReference))
        {
            return ReviewSanctions(hold, SanctionsStatus.SanctionsEscalated, escalated.DecisionReference);
        }

        return Ignore(hold);
    }

    private static Decision AuthorizeDebit(PricedTransfer price, DateTimeOffset observedAt)
    {
        if (price.Quote.ExpiresAt <= observedAt)
        {
            return Reject(RejectionReason.QuoteExpiredBeforeDebit);
        }

        var debitKey = Key(price, "debit");
        return Progress(new AwaitingDebit(price, debitKey), new Debit(debitKey, price), ProgressKind.ScreeningCleared);
    }

    private static Decision BookDebit(AwaitingDebit state, PaymentEvent message)
    {
        if (message is DebitRejected rejected && rejected.Key == state.DebitKey && HasReference(rejected.Reference))
        {
            return Reject(RejectionReason.DebitRejected, rejected.Reference);
        }
        if (message is not DebitPosted posted || posted.Key != state.DebitKey || !HasReference(posted.Reference))
        {
            return Ignore(state);
        }

        var escrowKey = Key(state.Price, "escrow");
        return Progress(new AwaitingEscrow(state.Price, posted.Reference, escrowKey),
            new HoldEscrow(escrowKey, posted.Reference, state.Price.Transfer.Usd), ProgressKind.DebitConfirmed);
    }

    private static Decision BookEscrow(AwaitingEscrow state, PaymentEvent message)
    {
        if (message is EscrowRejected rejected && rejected.Key == state.EscrowKey && HasReference(rejected.Reference))
        {
            return BeginRefund(new EscrowNeverHeld(state.Price, state.DebitReference, rejected.Reference),
                state.Price, state.DebitReference);
        }
        if (message is not EscrowHeld held || held.Key != state.EscrowKey || !HasReference(held.Reference))
        {
            return Ignore(state);
        }

        var key = Key(state.Price, "payout");
        var funds = new Funding(state.Price, state.DebitReference, held.Reference, key);
        return Progress(new PayoutPending(funds), new SubmitCredit(key, funds), ProgressKind.PayoutIntentRecorded);
    }

    private static Decision PayoutResult(PayoutPending state, PaymentEvent message)
    {
        var funds = state.Funds;
        if (message is CreditConfirmed paid && paid.Key == funds.PayoutKey && HasReference(paid.Reference))
        {
            var releaseKey = Key(funds.Price, "release-settled");
            return Progress(new Settling(funds, paid.Reference, releaseKey), new ReleaseEscrow(releaseKey, funds.EscrowReference), ProgressKind.CreditConfirmed);
        }

        if (message is CreditRejected rejected && rejected.Key == funds.PayoutKey && HasReference(rejected.Reference))
        {
            return RecordNonpayment(funds, rejected.Reference);
        }

        return message is CreditStillUnknown pending && pending.Key == funds.PayoutKey
            ? Progress(state, new NoAction(), ProgressKind.PayoutStillUnknown)
            : Ignore(state);
    }

    private static Decision RecordNonpayment(Funding funds, string reference)
    {
        var releaseKey = Key(funds.Price, "release-unwind");
        return Progress(new Unwinding(funds, reference, releaseKey), new ReleaseEscrow(releaseKey, funds.EscrowReference), ProgressKind.DefinitiveNonpayment);
    }

    private static Decision CompleteSettlement(Settling state, PaymentEvent message)
    {
        if (message is EscrowReleaseFailed failed && failed.Key == state.ReleaseKey && HasReference(failed.Reference))
        {
            return Freeze(state, new OperationFailure(PartnerOperation.EscrowRelease, failed.Key, failed.Reference));
        }

        return message is EscrowReleased released && released.Key == state.ReleaseKey
            ? Progress(new Settled(state.Funds, state.BankReference), new NoAction(), ProgressKind.Settled)
            : Ignore(state);
    }

    private static Decision CompleteUnwind(Unwinding state, PaymentEvent message)
    {
        if (message is EscrowReleaseFailed failed && failed.Key == state.ReleaseKey && HasReference(failed.Reference))
        {
            return Freeze(state, new OperationFailure(PartnerOperation.EscrowRelease, failed.Key, failed.Reference));
        }

        return message is EscrowReleased released && released.Key == state.ReleaseKey
            ? BeginRefund(new EscrowReleasedAfterNonpayment(state.Funds, state.RejectionReference, state.ReleaseKey),
                state.Funds.Price, state.Funds.DebitReference)
            : Ignore(state);
    }

    private static Decision BeginRefund(RefundBasis basis, PricedTransfer price, string debitReference)
    {
        var refundKey = Key(price, "refund");
        return Progress(new RefundPending(basis, refundKey),
            new Refund(refundKey, debitReference, price.Transfer.Usd), ProgressKind.RefundRequired);
    }

    private static Decision CompleteRefund(RefundPending state, PaymentEvent message)
    {
        if (message is RefundRejected failed && failed.Key == state.RefundKey && HasReference(failed.Reference))
        {
            return Freeze(state, new OperationFailure(PartnerOperation.Refund, failed.Key, failed.Reference));
        }

        return message is DebitRefunded refunded && refunded.Key == state.RefundKey && HasReference(refunded.Reference)
            ? Progress(new Refunded(state.Basis, refunded.Reference), new NoAction(), ProgressKind.RefundConfirmed)
            : Ignore(state);
    }

    private readonly union PayoutEvidence(NoRecordedPayout, RecordedCredit, RecordedNonpayment);
    private readonly record struct NoRecordedPayout;
    private readonly record struct RecordedCredit(string Key, string Reference);
    private readonly record struct RecordedNonpayment(string Key, string Reference);

    private static PayoutEvidence PriorPayout(SagaState state) => state switch
    {
        Settling settling => new RecordedCredit(settling.Funds.PayoutKey, settling.BankReference),
        Settled settled => new RecordedCredit(settled.Funds.PayoutKey, settled.BankReference),
        Unwinding unwind => new RecordedNonpayment(unwind.Funds.PayoutKey, unwind.RejectionReference),
        RefundPending pending => PriorRefundPayout(pending.Basis),
        Refunded refunded => PriorRefundPayout(refunded.Basis),
        PartnerReview review => PriorPayout(review.Previous),
        AwaitingQuote or AwaitingScreening or AwaitingDebit or AwaitingEscrow or PayoutPending or
            SanctionsHold or Rejected or Disputed => new NoRecordedPayout()
    };

    private static PayoutEvidence PriorRefundPayout(RefundBasis basis) => basis switch
    {
        EscrowReleasedAfterNonpayment released => new RecordedNonpayment(released.Funds.PayoutKey, released.RejectionReference),
        EscrowNeverHeld => new NoRecordedPayout()
    };

    private static Decision Dispute(CreditConflict conflict, string reportedReference) =>
        new(new Disputed(conflict), new Review(reportedReference), conflict);

    private static Decision Freeze(SagaState state, OperationFailure failure) =>
        new(new PartnerReview(state, failure), new Review(failure.Reference), failure);

    private static Decision ReconcileRelease(SagaState state, string key) =>
        Reconcile(state, new ReconcileEscrowRelease(key), PartnerOperation.EscrowRelease, key);

    private static Decision Reconcile(SagaState state, PaymentAction action, PartnerOperation operation, string key) =>
        new(state, action, new ReconciliationEvidence(operation, key));

    private static Decision ReviewSanctions(SanctionsHold state, SanctionsStatus status, string reference) =>
        new(state, new Review(reference), new SanctionsEvidence(status, state.CaseReference, reference));

    private static Decision Progress(SagaState state, PaymentAction action, ProgressKind kind) =>
        new(state, action, new ProgressEvidence(kind));

    private static Decision Reject(RejectionReason reason, string reference = "") =>
        new(new Rejected(reason), new NoAction(), new RejectionEvidence(reason, reference));

    private static Decision Ignore(SagaState state) => Progress(state, new NoAction(), ProgressKind.NoApplicableTransition);

    private static string Key(PricedTransfer price, string operation) => price.Transfer.Id + ":" + operation;

    private static bool HasReference(string reference) => !string.IsNullOrWhiteSpace(reference);
}
