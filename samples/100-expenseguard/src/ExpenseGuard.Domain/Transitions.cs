namespace ExpenseGuard.Domain;

public static class Transitions
{
    public static Transition Decide(ExpenseClaim c, Command command, Actor actor)
    {
        if (c.Receipt.Value is null || c.State.Value is null || c.Policy.Value is null || command.Value is null)
            return new Denied("InvalidEvidence");
        return command switch
        {
        Submit => SubmitClaim(c, actor),
        Revise revision => actor.Id == c.Employee && actor.Role == Role.Employee && c.State is Draft or NeedsMoreInfo &&
            !string.IsNullOrWhiteSpace(revision.Merchant) && revision.Merchant.Length <= 200 &&
            revision.Amount.Currency is "USD" or "EUR" or "GBP" && revision.Units is >= 1 and <= 366 && Enum.IsDefined(revision.Category)
            ? new Accepted(c with { Amount = revision.Amount, Category = revision.Category, Merchant = revision.Merchant,
                SpentOn = revision.SpentOn, Units = revision.Units, State = new Draft(), Receipt = new MissingReceipt(), Policy = new NoAssessment() }, "DraftRevised")
            : new Denied("ReviseNotAllowed"),
        Attach attach => AttachReceipt(c, attach.Receipt, actor),
        Check check => c.State is Submitted && actor.Id == c.Employee && actor.Role == Role.Employee
            ? new Accepted(c with { State = new PolicyChecked(), Policy = PolicyEngine.Evaluate(c, check.Policy, check.Facts) }, "PolicyEvaluated")
            : new Denied("CheckRequiresSubmittedOwner"),
        Apply => ApplyAssessment(c, actor),
        Approve => ApproveClaim(c, actor),
        Reject reject => CanReview(c, actor) && !string.IsNullOrWhiteSpace(reject.Reason)
            ? new Accepted(c with { State = new Rejected(reject.Reason) }, "Rejected") : new Denied("RejectNotAllowed"),
        Hold hold => actor.Role == Role.Auditor && !string.IsNullOrWhiteSpace(hold.Reason) && c.State is not (Draft or Reimbursed or AuditHeld)
            ? new Accepted(c with { State = new AuditHeld(hold.Reason) }, "AuditHeld") : new Denied("HoldNotAllowed"),
        Reimburse pay => actor.Role == Role.PaymentWorker && c.State is Approved && c.Policy is Assessment &&
            !string.IsNullOrWhiteSpace(pay.PaymentReference)
            ? new Accepted(c with { State = new Reimbursed(pay.PaymentReference) }, "Reimbursed") : new Denied("ReimburseRequiresApproval")
        };
    }

    private static Transition SubmitClaim(ExpenseClaim c, Actor actor) => actor.Id == c.Employee && actor.Role == Role.Employee &&
        c.State is Draft or NeedsMoreInfo
            ? new Accepted(c with { State = new Submitted(), Policy = new NoAssessment() }, "Submitted")
            : new Denied("SubmitNotAllowed");

    private static Transition AttachReceipt(ExpenseClaim c, Receipt receipt, Actor actor) =>
        actor.Id == c.Employee && actor.Role == Role.Employee && c.State is Draft or NeedsMoreInfo &&
        !string.IsNullOrWhiteSpace(receipt.Reference) && receipt.Reference.Length <= 500 &&
        receipt.Sha256.Length == 64 && receipt.Sha256.All(Uri.IsHexDigit)
            ? new Accepted(c with { Receipt = receipt, State = new Draft(), Policy = new NoAssessment() }, "ReceiptAttached")
            : new Denied("AttachNotAllowed");

    private static Transition ApplyAssessment(ExpenseClaim c, Actor actor)
    {
        if (c.State is not PolicyChecked || c.Policy is not Assessment a || actor.Id != c.Employee || actor.Role != Role.Employee)
            return new Denied("ApplyRequiresAssessment");
        ExpenseState next = a.Decision switch
        {
            Decision.Approve => new Approved(new("policy-engine")),
            Decision.Reject => new Rejected(a.Evidence[^1].Code),
            Decision.MoreInfo => new NeedsMoreInfo(a.Evidence[^1].Code),
            Decision.Manager => new ManagerApprovalRequired(ApprovalRoute.ManagerOnly),
            Decision.ManagerThenFinance => new ManagerApprovalRequired(ApprovalRoute.ManagerThenFinance),
            Decision.Hold => new AuditHeld(a.Evidence[^1].Code),
            _ => new AuditHeld("UnknownPolicyDecision")
        };
        return new Accepted(c with { State = next }, "PolicyApplied");
    }

    private static Transition ApproveClaim(ExpenseClaim c, Actor actor)
    {
        if (actor.Id == c.Employee || c.Policy is not Assessment) return new Denied("SelfApprovalOrMissingAssessment");
        return c.State switch
        {
            ManagerApprovalRequired { Route: ApprovalRoute.ManagerThenFinance } when actor.Role == Role.Manager =>
                new Accepted(c with { State = new FinanceApprovalRequired(actor.Id) }, "ManagerApproved"),
            ManagerApprovalRequired { Route: ApprovalRoute.ManagerOnly } when actor.Role == Role.Manager =>
                new Accepted(c with { State = new Approved(actor.Id) }, "ManagerApproved"),
            FinanceApprovalRequired finance when actor.Role == Role.Finance && actor.Id != finance.Manager =>
                new Accepted(c with { State = new Approved(actor.Id) }, "FinanceApproved"),
            _ => new Denied("ApprovalNotAllowed")
        };
    }

    private static bool CanReview(ExpenseClaim c, Actor actor) => actor.Id != c.Employee && c.State switch
    {
        ManagerApprovalRequired => actor.Role == Role.Manager,
        FinanceApprovalRequired f => actor.Role == Role.Finance && actor.Id != f.Manager,
        _ => false
    };
}
