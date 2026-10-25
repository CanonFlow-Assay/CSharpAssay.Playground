using System.Collections.Immutable;

namespace ExpenseGuard.Domain;

public sealed record ExpenseId(Guid Value);
public sealed record EmployeeId(string Value);
public sealed record Money(decimal Value, string Currency);
public sealed record MissingReceipt;
// Opaque storage reference, never fetched by the policy engine or API.
public sealed record Receipt(string Reference, string Sha256, bool Itemized);
public readonly union ReceiptEvidence(MissingReceipt, Receipt);
public enum Category { Meal, Transport, Hotel, Software, Other, Personal }
public enum Role { Employee, Manager, Finance, Auditor, PaymentWorker }
public sealed record Actor(EmployeeId Id, Role Role);
public enum ApprovalRoute { ManagerOnly, ManagerThenFinance }
public sealed record Draft;
public sealed record Submitted;
public sealed record PolicyChecked;
public sealed record NeedsMoreInfo(string Code);
public sealed record ManagerApprovalRequired(ApprovalRoute Route);
public sealed record FinanceApprovalRequired(EmployeeId Manager);
public sealed record Approved(EmployeeId Approver);
public sealed record Rejected(string Code);
public sealed record Reimbursed(string PaymentReference);
public sealed record AuditHeld(string Code);
public readonly union ExpenseState(Draft, Submitted, PolicyChecked, NeedsMoreInfo,
    ManagerApprovalRequired, FinanceApprovalRequired, Approved, Rejected, Reimbursed, AuditHeld);
public sealed record NoAssessment;
public enum Decision { Approve, Reject, MoreInfo, Manager, ManagerThenFinance, Hold }
public sealed record RuleEvidence(string Rule, string Code, string Detail);
public sealed record Assessment(string PolicyVersion, DateOnly EvaluatedOn, decimal BaseAmount,
    string BaseCurrency, string FxSource, decimal RateUsed, decimal PriorMealsBase, bool Duplicate,
    Decision Decision, ImmutableArray<RuleEvidence> Evidence);
public readonly union PolicyEvidence(NoAssessment, Assessment);
public sealed record ExpenseClaim(ExpenseId Id, EmployeeId Employee, Money Amount, Category Category,
    string Merchant, DateOnly SpentOn, int Units, ReceiptEvidence Receipt, ExpenseState State,
    PolicyEvidence Policy);
public sealed record RateMissing;
public sealed record ExchangeRate(string From, string To, decimal Rate, DateOnly On, string Source);
public readonly union RateEvidence(RateMissing, ExchangeRate);
public sealed record PolicyFacts(DateOnly Today, RateEvidence Rate, bool Duplicate, decimal PriorMealsBase,
    bool ReceiptBytesVerified = false);
public sealed record ExpensePolicy(string Version, string BaseCurrency, string FxSource,
    decimal ReceiptThreshold, decimal MealDailyLimit, decimal TransportLimit, decimal HotelNightLimit,
    decimal SoftwareMonthLimit, decimal ManagerThreshold, decimal FinanceThreshold, int MaximumAgeDays);
public static class Policies
{
    public static ExpensePolicy Demo { get; } = new("expense-v2", "USD", "treasury-demo-v1",
        25m, 50m, 100m, 200m, 100m, 200m, 1000m, 90);
}
public sealed record Accepted(ExpenseClaim Claim, string Event);
public sealed record Denied(string Code);
public readonly union Transition(Accepted, Denied);
public sealed record Submit;
public sealed record Revise(Money Amount, Category Category, string Merchant, DateOnly SpentOn, int Units);
public sealed record Attach(Receipt Receipt);
public sealed record Check(ExpensePolicy Policy, PolicyFacts Facts);
public sealed record Apply;
public sealed record Approve;
public sealed record Reject(string Reason);
public sealed record Hold(string Reason);
public sealed record Reimburse(string PaymentReference);
public readonly union Command(Submit, Revise, Attach, Check, Apply, Approve, Reject, Hold, Reimburse);

public static class StateNames
{
    // Deliberately exhaustive: adding a union member must update this switch.
    public static string Of(ExpenseState state) => state.Value is null ? "Invalid" : state switch
    {
        Draft => "Draft", Submitted => "Submitted", PolicyChecked => "PolicyChecked",
        NeedsMoreInfo => "NeedsMoreInfo", ManagerApprovalRequired => "ManagerApprovalRequired",
        FinanceApprovalRequired => "FinanceApprovalRequired", Approved => "Approved",
        Rejected => "Rejected", Reimbursed => "Reimbursed", AuditHeld => "AuditHeld"
    };
}
