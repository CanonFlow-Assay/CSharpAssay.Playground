using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExpenseGuard.Domain;

namespace ExpenseGuard.Infrastructure;

// Union values are converted exhaustively at this shell boundary; persistence
// never relies on a preview compiler's generated union serialization layout.
public sealed record ClaimDocument(Guid Id, string Employee, decimal Amount, string Currency, Category Category,
    string Merchant, DateOnly SpentOn, int Units, Receipt? Receipt, string State, string StateDetail,
    ApprovalRoute Route, Assessment? Policy)
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true
    };
    public static ClaimDocument From(ExpenseClaim c)
    {
        var detail = c.State switch
        {
            Draft or Submitted or PolicyChecked or ManagerApprovalRequired => "",
            NeedsMoreInfo n => n.Code, FinanceApprovalRequired f => f.Manager.Value,
            Approved a => a.Approver.Value, Rejected r => r.Code,
            Reimbursed r => r.PaymentReference, AuditHeld h => h.Code
        };
        return new(c.Id.Value, c.Employee.Value, c.Amount.Value, c.Amount.Currency, c.Category,
            c.Merchant, c.SpentOn, c.Units,
            c.Receipt switch { MissingReceipt => null, Receipt r => r }, StateNames.Of(c.State), detail,
            c.State is ManagerApprovalRequired m ? m.Route : ApprovalRoute.ManagerOnly,
            c.Policy switch { NoAssessment => null, Assessment a => a });
    }
    public ExpenseClaim ToDomain()
    {
        ExpenseState state = State switch
        {
            "Draft" => new Draft(), "Submitted" => new Submitted(), "PolicyChecked" => new PolicyChecked(),
            "NeedsMoreInfo" => new NeedsMoreInfo(StateDetail), "ManagerApprovalRequired" => new ManagerApprovalRequired(Route),
            "FinanceApprovalRequired" => new FinanceApprovalRequired(new(StateDetail)), "Approved" => new Approved(new(StateDetail)),
            "Rejected" => new Rejected(StateDetail), "Reimbursed" => new Reimbursed(StateDetail), "AuditHeld" => new AuditHeld(StateDetail),
            _ => throw new InvalidDataException("Unknown persisted expense state.")
        };
        return new(new(Id), new(Employee), new(Amount, Currency), Category, Merchant, SpentOn, Units,
            Receipt is Receipt r ? r : new MissingReceipt(), state, Policy is Assessment a ? a : new NoAssessment());
    }
    public string Serialize() => JsonSerializer.Serialize(this, Json);
    public static ClaimDocument Parse(string json) => JsonSerializer.Deserialize<ClaimDocument>(json, Json)
        ?? throw new InvalidDataException("Missing claim snapshot.");
}
