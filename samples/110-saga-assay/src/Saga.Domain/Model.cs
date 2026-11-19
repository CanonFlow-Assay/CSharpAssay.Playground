namespace Playground.Saga.Domain;

public sealed record Transfer(string Id, decimal Usd);
public sealed record Quote(string Id, string From, string To, decimal Rate, DateTimeOffset ExpiresAt);
public sealed record PricedTransfer(Transfer Transfer, Quote Quote, decimal Eur);
public sealed record Funding(PricedTransfer Price, string DebitReference, string EscrowReference, string PayoutKey);

public readonly union SagaState(AwaitingQuote, AwaitingScreening, AwaitingDebit, AwaitingEscrow,
    PayoutPending, Settling, Unwinding, RefundPending, Settled, Refunded, Rejected, SanctionsHold, Disputed, PartnerReview);

public readonly record struct AwaitingQuote(Transfer Transfer);
public readonly record struct AwaitingScreening(PricedTransfer Price);
public readonly record struct AwaitingDebit(PricedTransfer Price, string DebitKey);
public readonly record struct AwaitingEscrow(PricedTransfer Price, string DebitReference, string EscrowKey);
public readonly record struct PayoutPending(Funding Funds);
public readonly record struct Settling(Funding Funds, string BankReference, string ReleaseKey);
public readonly record struct Unwinding(Funding Funds, string RejectionReference, string ReleaseKey);
public readonly record struct RefundPending(RefundBasis Basis, string RefundKey);
public readonly record struct Settled(Funding Funds, string BankReference);
public readonly record struct Refunded(RefundBasis Basis, string RefundReference);
public readonly record struct Rejected(RejectionReason Reason);
public readonly record struct SanctionsHold(PricedTransfer Price, string ScreenKey, string CaseReference);
public readonly record struct Disputed(CreditConflict Conflict);
public sealed record PartnerReview(SagaState Previous, OperationFailure Failure);

public readonly union RefundBasis(EscrowNeverHeld, EscrowReleasedAfterNonpayment);
public readonly record struct EscrowNeverHeld(PricedTransfer Price, string DebitReference, string RejectionReference);
public readonly record struct EscrowReleasedAfterNonpayment(Funding Funds, string RejectionReference, string ReleaseKey);

public readonly union PaymentEvent(QuoteReceived, QuoteUnavailable, ScreeningCleared, ScreeningHeld, DebitPosted,
    DebitRejected, EscrowHeld, EscrowRejected, CreditConfirmed, CreditRejected, CreditStillUnknown,
    EscrowReleased, EscrowReleaseFailed, DebitRefunded, RefundRejected, SanctionsCleared, SanctionsDenied, SanctionsEscalated, TimedOut);

public readonly record struct QuoteReceived(string TransferId, Quote Quote);
public readonly record struct QuoteUnavailable(string TransferId);
public readonly record struct ScreeningCleared(string Key, string Reference);
public readonly record struct ScreeningHeld(string Key, string CaseReference);
public readonly record struct DebitPosted(string Key, string Reference);
public readonly record struct DebitRejected(string Key, string Reference);
public readonly record struct EscrowHeld(string Key, string Reference);
public readonly record struct EscrowRejected(string Key, string Reference);
public readonly record struct CreditConfirmed(string Key, string Reference);
public readonly record struct CreditRejected(string Key, string Reference);
public readonly record struct CreditStillUnknown(string Key);
public readonly record struct EscrowReleased(string Key);
public readonly record struct EscrowReleaseFailed(string Key, string Reference);
public readonly record struct DebitRefunded(string Key, string Reference);
public readonly record struct RefundRejected(string Key, string Reference);
public readonly record struct SanctionsCleared(string Key, string CaseReference, string DecisionReference);
public readonly record struct SanctionsDenied(string Key, string CaseReference, string DecisionReference);
public readonly record struct SanctionsEscalated(string Key, string CaseReference, string DecisionReference);
public readonly record struct TimedOut;

public readonly union PaymentAction(NoAction, RequestQuote, Screen, Debit, HoldEscrow, SubmitCredit,
    ReconcileCredit, ReconcileDebit, ReconcileEscrow, ReconcileEscrowRelease, ReconcileRefund, ReleaseEscrow, Refund, Review);

public readonly record struct NoAction;
public readonly record struct RequestQuote(string TransferId);
public readonly record struct Screen(string Key, PricedTransfer Price);
public readonly record struct Debit(string Key, PricedTransfer Price);
public readonly record struct HoldEscrow(string Key, string DebitReference, decimal Usd);
public readonly record struct SubmitCredit(string Key, Funding Funds);
public readonly record struct ReconcileCredit(string PayoutKey);
public readonly record struct ReconcileDebit(string DebitKey);
public readonly record struct ReconcileEscrow(string EscrowKey);
public readonly record struct ReconcileEscrowRelease(string ReleaseKey);
public readonly record struct ReconcileRefund(string RefundKey);
public readonly record struct ReleaseEscrow(string Key, string EscrowReference);
public readonly record struct Refund(string Key, string DebitReference, decimal Usd);
public readonly record struct Review(string Reference);

public enum ProgressKind
{
    QuoteRequested, QuoteAccepted, ScreeningCleared, DebitConfirmed, PayoutIntentRecorded,
    CreditConfirmed, DefinitiveNonpayment, PayoutStillUnknown, Settled, RefundRequired, RefundConfirmed, NoApplicableTransition
}

public enum RejectionReason
{
    InvalidUsdAmountOrIdentity, TimedOutBeforeDebit, QuoteUnavailable, QuoteInvalidOrExpired,
    RoundedPayoutIsZero, QuoteExpiredBeforeDebit, DebitRejected, SanctionsDenied
}

public enum PartnerOperation { Debit, EscrowHold, Credit, EscrowRelease, Refund }

public enum SanctionsStatus { SanctionsHold, SanctionsHoldNeedsReview, SanctionsEscalated }

public readonly union SagaEvidence(ProgressEvidence, RejectionEvidence, ReconciliationEvidence, SanctionsEvidence, OperationFailure, CreditConflict);

public readonly record struct ProgressEvidence(ProgressKind Kind);
public readonly record struct RejectionEvidence(RejectionReason Reason, string Reference);
public readonly record struct ReconciliationEvidence(PartnerOperation Operation, string Key);
public readonly record struct SanctionsEvidence(SanctionsStatus Status, string CaseReference, string Reference);
public readonly record struct OperationFailure(PartnerOperation Operation, string Key, string Reference);
public readonly record struct CreditConflict(string Key, string CreditReference, string NonpaymentReference);

public sealed record Decision(SagaState State, PaymentAction Action, SagaEvidence Evidence);
