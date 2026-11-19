namespace Playground.Saga.Domain;

public static class PaymentSaga
{
    public static Decision Start(Transfer transfer) =>
        string.IsNullOrWhiteSpace(transfer.Id) || transfer.Usd is <= 0m or > 1_000_000m ||
        decimal.Round(transfer.Usd, 2, MidpointRounding.ToEven) != transfer.Usd
            ? Reject("InvalidUsdAmountOrIdentity")
            : new(new AwaitingQuote(transfer), new RequestQuote(transfer.Id), "QuoteRequested");

    public static Decision Apply(SagaState state, PaymentEvent message, DateTimeOffset observedAt) =>
        message is TimedOut ? OnTimeout(state) : state switch
        {
            AwaitingQuote awaiting => Price(awaiting, message, observedAt),
            AwaitingScreening awaiting => ScreenResult(awaiting, message, observedAt),
            AwaitingDebit awaiting => BookDebit(awaiting, message),
            AwaitingEscrow awaiting => BookEscrow(awaiting, message),
            PayoutPending awaiting => PayoutResult(awaiting, message),
            Settling awaiting => CompleteSettlement(awaiting, message),
            Unwinding awaiting => BeginRefund(awaiting, message),
            RefundPending awaiting => CompleteRefund(awaiting, message),
            Refunded refunded => AfterRefund(refunded, message),
            Settled or Rejected or SanctionsHold or Disputed => Ignore(state)
        };

    private static Decision OnTimeout(SagaState state) => state switch
    {
        AwaitingQuote or AwaitingScreening => Reject("TimedOutBeforeDebit"),
        PayoutPending pending => new(state, new ReconcileCredit(pending.Funds.PayoutKey), "PayoutOutcomeUnknown"),
        AwaitingDebit or AwaitingEscrow or Settling or Unwinding or RefundPending =>
            new(state, new NoAction(), "AwaitingPartnerConfirmation"),
        Settled or Refunded or Rejected or Disputed => Ignore(state),
        SanctionsHold hold => new(state, new Review(hold.CaseReference), "SanctionsHoldNeedsReview")
    };

    private static Decision Price(AwaitingQuote state, PaymentEvent message, DateTimeOffset observedAt)
    {
        if (message is QuoteUnavailable unavailable && unavailable.TransferId == state.Transfer.Id)
        {
            return Reject("QuoteUnavailable");
        }

        if (message is not QuoteReceived received || received.TransferId != state.Transfer.Id)
        {
            return Ignore(state);
        }

        var quote = received.Quote;
        if (string.IsNullOrWhiteSpace(quote.Id) || quote.From != "USD" || quote.To != "EUR" ||
            quote.Rate is <= 0m or > 100m || quote.ExpiresAt <= observedAt)
        {
            return Reject("QuoteInvalidOrExpired");
        }

        var eur = decimal.Round(state.Transfer.Usd * quote.Rate, 2, MidpointRounding.ToEven);
        if (eur <= 0m)
        {
            return Reject("RoundedPayoutIsZero");
        }

        var price = new PricedTransfer(state.Transfer, quote, eur);
        return new(new AwaitingScreening(price), new Screen(Key(price, "screen"), price), "QuoteAccepted");
    }

    private static Decision ScreenResult(AwaitingScreening state, PaymentEvent message, DateTimeOffset observedAt)
    {
        var expected = Key(state.Price, "screen");
        if (message is ScreeningHeld held && held.Key == expected && HasReference(held.CaseReference))
        {
            return new(new SanctionsHold(held.CaseReference), new Review(held.CaseReference), "SanctionsHold");
        }

        if (message is not ScreeningCleared cleared || cleared.Key != expected || !HasReference(cleared.Reference))
        {
            return Ignore(state);
        }

        if (state.Price.Quote.ExpiresAt <= observedAt)
        {
            return Reject("QuoteExpiredBeforeDebit");
        }

        var debitKey = Key(state.Price, "debit");
        return new(new AwaitingDebit(state.Price, debitKey), new Debit(debitKey, state.Price), "ScreeningCleared");
    }

    private static Decision BookDebit(AwaitingDebit state, PaymentEvent message)
    {
        if (message is not DebitPosted posted || posted.Key != state.DebitKey || !HasReference(posted.Reference))
        {
            return Ignore(state);
        }

        var escrowKey = Key(state.Price, "escrow");
        return new(new AwaitingEscrow(state.Price, posted.Reference, escrowKey),
            new HoldEscrow(escrowKey, posted.Reference, state.Price.Transfer.Usd), "DebitConfirmed");
    }

    private static Decision BookEscrow(AwaitingEscrow state, PaymentEvent message)
    {
        if (message is not EscrowHeld held || held.Key != state.EscrowKey || !HasReference(held.Reference))
        {
            return Ignore(state);
        }

        var key = Key(state.Price, "payout");
        var funds = new Funding(state.Price, state.DebitReference, held.Reference, key);
        return new(new PayoutPending(funds), new SubmitCredit(key, funds), "PayoutIntentRecorded");
    }

    private static Decision PayoutResult(PayoutPending state, PaymentEvent message)
    {
        var funds = state.Funds;
        if (message is CreditConfirmed paid && paid.Key == funds.PayoutKey && HasReference(paid.Reference))
        {
            var releaseKey = Key(funds.Price, "release-settled");
            return new(new Settling(funds, paid.Reference, releaseKey), new ReleaseEscrow(releaseKey, funds.EscrowReference), "CreditConfirmed");
        }

        if (message is CreditRejected rejected && rejected.Key == funds.PayoutKey && HasReference(rejected.Reference))
        {
            var releaseKey = Key(funds.Price, "release-unwind");
            return new(new Unwinding(funds, rejected.Reference, releaseKey), new ReleaseEscrow(releaseKey, funds.EscrowReference), "DefinitiveNonpayment");
        }

        return message is CreditStillUnknown pending && pending.Key == funds.PayoutKey
            ? new(state, new NoAction(), "PayoutStillUnknown")
            : Ignore(state);
    }

    private static Decision CompleteSettlement(Settling state, PaymentEvent message) =>
        message is EscrowReleased released && released.Key == state.ReleaseKey
            ? new(new Settled(state.Funds, state.BankReference), new NoAction(), "Settled")
            : Ignore(state);

    private static Decision BeginRefund(Unwinding state, PaymentEvent message)
    {
        if (message is CreditConfirmed paid && paid.Key == state.Funds.PayoutKey && HasReference(paid.Reference))
        {
            return Dispute(state.Funds, paid.Reference);
        }

        if (message is not EscrowReleased released || released.Key != state.ReleaseKey)
        {
            return Ignore(state);
        }

        var refundKey = Key(state.Funds.Price, "refund");
        return new(new RefundPending(state.Funds, state.RejectionReference, refundKey),
            new Refund(refundKey, state.Funds.DebitReference, state.Funds.Price.Transfer.Usd), "RefundRequired");
    }

    private static Decision CompleteRefund(RefundPending state, PaymentEvent message)
    {
        if (message is CreditConfirmed paid && paid.Key == state.Funds.PayoutKey && HasReference(paid.Reference))
        {
            return Dispute(state.Funds, paid.Reference);
        }

        return message is DebitRefunded refunded && refunded.Key == state.RefundKey && HasReference(refunded.Reference)
            ? new(new Refunded(state.Funds, refunded.Reference), new NoAction(), "RefundConfirmed")
            : Ignore(state);
    }

    private static Decision AfterRefund(Refunded state, PaymentEvent message) =>
        message is CreditConfirmed paid && paid.Key == state.Funds.PayoutKey && HasReference(paid.Reference)
            ? Dispute(state.Funds, paid.Reference)
            : Ignore(state);

    private static Decision Dispute(Funding funds, string reference) =>
        new(new Disputed(funds, reference), new Review(reference), "ContradictoryProviderOutcome");

    private static Decision Reject(string reason) => new(new Rejected(reason), new NoAction(), reason);

    private static Decision Ignore(SagaState state) => new(state, new NoAction(), "NoApplicableTransition");

    private static string Key(PricedTransfer price, string operation) => price.Transfer.Id + ":" + operation;

    private static bool HasReference(string reference) => !string.IsNullOrWhiteSpace(reference);
}
