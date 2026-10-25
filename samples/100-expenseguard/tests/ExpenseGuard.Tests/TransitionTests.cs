using ExpenseGuard.Domain;
using ExpenseGuard.Infrastructure;

namespace ExpenseGuard.Tests;

public sealed class TransitionTests
{
    public static IEnumerable<object[]> States()
    {
        ExpenseState[] states = [new Draft(), new Submitted(), new PolicyChecked(), new NeedsMoreInfo("missing"),
            new ManagerApprovalRequired(ApprovalRoute.ManagerOnly), new FinanceApprovalRequired(Fixtures.Manager.Id),
            new Approved(Fixtures.Manager.Id), new Rejected("reason"), new Reimbursed("payment"), new AuditHeld("duplicate")];
        return states.Select(s => new object[] { StateNames.Of(s) });
    }
    private static ExpenseState State(string name) => new ClaimDocument(Guid.NewGuid(), "alice", 20, "USD", Category.Other,
        "merchant", Fixtures.Today, 1, null, name, "detail", ApprovalRoute.ManagerOnly, null).ToDomain().State;

    [Theory] [MemberData(nameof(States))]
    public void Reimbursement_requires_approved_state(string name)
    {
        var c = Fixtures.Assess(Fixtures.Claim()) with { State = State(name) };
        var result = Transitions.Decide(c, new Reimburse("SIMULATED-ref"), new(new("worker"), Role.PaymentWorker));
        Assert.Equal(name == "Approved", result is Accepted);
    }
    [Theory] [MemberData(nameof(States))]
    public void Human_approval_cannot_skip_states(string name)
    {
        var c = Fixtures.Assess(Fixtures.Claim()) with { State = State(name) };
        Assert.Equal(name == "ManagerApprovalRequired", Transitions.Decide(c, new Approve(), Fixtures.Manager) is Accepted);
    }
    [Theory] [MemberData(nameof(States))]
    public void State_roundtrip_is_explicit(string name)
    {
        var c = Fixtures.Claim() with { State = State(name) };
        Assert.Equal(name, StateNames.Of(ClaimDocument.Parse(ClaimDocument.From(c).Serialize()).ToDomain().State));
    }
    [Theory] [MemberData(nameof(States))]
    public void Revision_only_allows_draft_or_missing_information(string name)
    {
        var c = Fixtures.Assess(Fixtures.Claim()) with { State = State(name), Receipt = Fixtures.Receipt() };
        var result = Transitions.Decide(c, new Revise(new(40, "USD"), Category.Other, "Revised merchant", Fixtures.Today, 1), Fixtures.Employee);
        Assert.Equal(name is "Draft" or "NeedsMoreInfo", result is Accepted);
        if (result is Accepted changed)
        {
            Assert.IsType<Draft>(changed.Claim.State.Value); Assert.IsType<NoAssessment>(changed.Claim.Policy.Value);
            Assert.IsType<MissingReceipt>(changed.Claim.Receipt.Value); Assert.Equal(40, changed.Claim.Amount.Value);
        }
    }
    [Fact] public void Missing_receipt_cannot_reach_approved() => Assert.IsType<NeedsMoreInfo>(Fixtures.Assess(Fixtures.Claim(300)).State.Value);
    [Fact] public void Manager_then_distinct_finance()
    {
        var c = Fixtures.Assess(Fixtures.Claim(1001) with { Receipt = Fixtures.Receipt() });
        Assert.IsType<ManagerApprovalRequired>(c.State.Value);
        Assert.IsType<Denied>(Transitions.Decide(c, new Approve(), Fixtures.Finance).Value);
        c = Assert.IsType<Accepted>(Transitions.Decide(c, new Approve(), Fixtures.Manager).Value).Claim;
        Assert.IsType<FinanceApprovalRequired>(c.State.Value);
        Assert.IsType<Denied>(Transitions.Decide(c, new Approve(), Fixtures.Finance with { Id = Fixtures.Manager.Id }).Value);
        Assert.IsType<Approved>(Assert.IsType<Accepted>(Transitions.Decide(c, new Approve(), Fixtures.Finance).Value).Claim.State.Value);
    }
    [Fact] public void Self_approval_is_denied()
    {
        var c = Fixtures.Assess(Fixtures.Claim(300) with { Receipt = Fixtures.Receipt() });
        Assert.IsType<Denied>(Transitions.Decide(c, new Approve(), Fixtures.Manager with { Id = c.Employee }).Value);
    }
    [Fact] public void Missing_information_can_be_supplied_and_rechecked()
    {
        var c = Fixtures.Assess(Fixtures.Claim(40));
        c = Assert.IsType<Accepted>(Transitions.Decide(c, new Attach(Fixtures.Receipt()), Fixtures.Employee).Value).Claim;
        c = Assert.IsType<Accepted>(Transitions.Decide(c, new Submit(), Fixtures.Employee).Value).Claim;
        Assert.IsType<Approved>(Fixtures.Assess(c).State.Value);
    }
    [Fact] public void Claimant_cannot_act_as_auditor()
    {
        var c = Fixtures.Assess(Fixtures.Claim());
        Assert.IsType<Denied>(Transitions.Decide(c, new Hold("check"), Fixtures.Employee).Value);
        Assert.IsType<AuditHeld>(Assert.IsType<Accepted>(Transitions.Decide(c, new Hold("check"), Fixtures.Auditor).Value).Claim.State.Value);
    }
    [Fact] public void Invalid_receipt_hash_is_denied() => Assert.IsType<Denied>(Transitions.Decide(
        Fixtures.Claim() with { State = new Draft() }, new Attach(new("ref", "not-a-hash", true)), Fixtures.Employee).Value);
    [Fact] public void Architecture_keeps_effectful_dependencies_out_of_domain()
    {
        var references = typeof(ExpenseClaim).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
        Assert.DoesNotContain(references, n => n.StartsWith("Npgsql", StringComparison.Ordinal) || n.Contains("EntityFramework", StringComparison.Ordinal) ||
            n.Contains("AspNetCore", StringComparison.Ordinal) || n.StartsWith("ExpenseGuard.Application", StringComparison.Ordinal) || n.StartsWith("ExpenseGuard.Infrastructure", StringComparison.Ordinal));
        Assert.All(typeof(ExpenseClaim).GetProperties(), p => Assert.True(p.SetMethod is null ||
            p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))));
    }
}
