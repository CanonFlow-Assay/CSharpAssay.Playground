namespace Playground.Saga.Domain;

public sealed record Transfer(string Id, decimal Usd);
public sealed record Quote(string Id, string From, string To, decimal Rate, DateTimeOffset ExpiresAt);
public sealed record PricedTransfer(Transfer Transfer, Quote Quote, decimal Eur);
public sealed record Funding(PricedTransfer Price, string DebitReference, string EscrowReference, string PayoutKey);

public readonly union SagaState(AwaitingQuote, AwaitingScreening, AwaitingDebit, AwaitingEscrow,
    PayoutPending, Settling, Unwinding, RefundPending, Settled, Refunded, Rejected, SanctionsHold, Disputed);

public readonly record struct AwaitingQuote(Transfer Transfer);
public readonly record struct AwaitingScreening(PricedTransfer Price);
public readonly record struct AwaitingDebit(PricedTransfer Price, string DebitKey);
public readonly record struct AwaitingEscrow(PricedTransfer Price, string DebitReference, string EscrowKey);
public readonly record struct PayoutPending(Funding Funds);
public readonly record struct Settling(Funding Funds, string BankReference, string ReleaseKey);
public readonly record struct Unwinding(Funding Funds, string RejectionReference, string ReleaseKey);
public readonly record struct RefundPending(Funding Funds, string RejectionReference, string RefundKey);
public readonly record struct Settled(Funding Funds, string BankReference);
public readonly record struct Refunded(Funding Funds, string RefundReference);
public readonly record struct Rejected(string Reason);
public readonly record struct SanctionsHold(string CaseReference);
public readonly record struct Disputed(Funding Funds, string BankReference);

public readonly union PaymentEvent(QuoteReceived, QuoteUnavailable, ScreeningCleared, ScreeningHeld, DebitPosted,
    EscrowHeld, CreditConfirmed, CreditRejected, CreditStillUnknown, EscrowReleased, DebitRefunded, TimedOut);

public readonly record struct QuoteReceived(string TransferId, Quote Quote);
public readonly record struct QuoteUnavailable(string TransferId);
public readonly record struct ScreeningCleared(string Key, string Reference);
public readonly record struct ScreeningHeld(string Key, string CaseReference);
public readonly record struct DebitPosted(string Key, string Reference);
public readonly record struct EscrowHeld(string Key, string Reference);
public readonly record struct CreditConfirmed(string Key, string Reference);
public readonly record struct CreditRejected(string Key, string Reference);
public readonly record struct CreditStillUnknown(string Key);
public readonly record struct EscrowReleased(string Key);
public readonly record struct DebitRefunded(string Key, string Reference);
public readonly record struct TimedOut;

public readonly union PaymentAction(NoAction, RequestQuote, Screen, Debit, HoldEscrow, SubmitCredit,
    ReconcileCredit, ReleaseEscrow, Refund, Review);

public readonly record struct NoAction;
public readonly record struct RequestQuote(string TransferId);
public readonly record struct Screen(string Key, PricedTransfer Price);
public readonly record struct Debit(string Key, PricedTransfer Price);
public readonly record struct HoldEscrow(string Key, string DebitReference, decimal Usd);
public readonly record struct SubmitCredit(string Key, Funding Funds);
public readonly record struct ReconcileCredit(string PayoutKey);
public readonly record struct ReleaseEscrow(string Key, string EscrowReference);
public readonly record struct Refund(string Key, string DebitReference, decimal Usd);
public readonly record struct Review(string Reference);

public sealed record Decision(SagaState State, PaymentAction Action, string Evidence);
