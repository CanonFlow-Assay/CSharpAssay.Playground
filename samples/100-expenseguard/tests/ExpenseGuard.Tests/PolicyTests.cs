using System.Collections.Immutable;
using ExpenseGuard.Domain;

namespace ExpenseGuard.Tests;

public static class Fixtures
{
    public static readonly DateOnly Today = new(2026, 10, 24);
    public static readonly Actor Employee = new(new("alice"), Role.Employee);
    public static readonly Actor Manager = new(new("manager"), Role.Manager);
    public static readonly Actor Finance = new(new("finance"), Role.Finance);
    public static readonly Actor Auditor = new(new("auditor"), Role.Auditor);
    public static ExpenseClaim Claim(decimal amount = 20m, Category category = Category.Other) =>
        new(new(Guid.NewGuid()), Employee.Id, new(amount, "USD"), category, "Test merchant", Today, 1,
            new MissingReceipt(), new Submitted(), new NoAssessment());
    public static Receipt Receipt(bool itemized = true) => new("receipt:fixture", new string('a', 64), itemized);
    public static PolicyFacts Facts => new(Today, new RateMissing(), false, 0m, ReceiptBytesVerified: true);
    public static ExpenseClaim Assess(ExpenseClaim c)
    {
        var checkedClaim = Assert.IsType<Accepted>(Transitions.Decide(c, new Check(Policies.Demo, Facts), Employee).Value);
        return Assert.IsType<Accepted>(Transitions.Decide(checkedClaim.Claim, new Apply(), Employee).Value).Claim;
    }
}

public sealed class PolicyTests
{
    public static TheoryData<decimal, bool, Decision> ThresholdCases => new()
    {
        {25m, false, Decision.Approve}, {25.01m, false, Decision.MoreInfo}, {25.01m, true, Decision.Approve},
        {200m, true, Decision.Approve}, {200.01m, true, Decision.Manager}, {1000m, true, Decision.Manager},
        {1000.01m, true, Decision.ManagerThenFinance}
    };
    [Theory] [MemberData(nameof(ThresholdCases))]
    public void Exact_thresholds(decimal amount, bool receipt, Decision expected)
    {
        var c = Fixtures.Claim(amount);
        if (receipt) c = c with { Receipt = Fixtures.Receipt() };
        Assert.Equal(expected, PolicyEngine.Evaluate(c, Policies.Demo, Fixtures.Facts).Decision);
    }

    public static TheoryData<Category, decimal, int, Decision> CategoryCases => new()
    {
        {Category.Meal, 50m, 1, Decision.Approve}, {Category.Meal, 50.01m, 1, Decision.Manager},
        {Category.Transport, 100m, 1, Decision.Approve}, {Category.Transport, 100.01m, 1, Decision.Manager},
        {Category.Hotel, 200m, 1, Decision.Approve}, {Category.Hotel, 200.01m, 1, Decision.Manager},
        {Category.Hotel, 200m, 2, Decision.Approve}, {Category.Software, 100m, 1, Decision.Approve},
        {Category.Software, 100.01m, 1, Decision.Manager}, {Category.Software, 150m, 2, Decision.Approve},
        {Category.Personal, 10m, 1, Decision.Reject}
    };
    [Theory] [MemberData(nameof(CategoryCases))]
    public void Category_limits(Category category, decimal amount, int units, Decision expected)
    {
        var c = Fixtures.Claim(amount, category) with { Receipt = Fixtures.Receipt(), Units = units };
        Assert.Equal(expected, PolicyEngine.Evaluate(c, Policies.Demo, Fixtures.Facts).Decision);
    }

    public static TheoryData<decimal, decimal, bool, Decision> MealCases => new()
    {
        {50m, 0m, false, Decision.Approve}, {50.01m, 0m, false, Decision.MoreInfo},
        {30m, 20m, false, Decision.Approve}, {30m, 20.01m, false, Decision.MoreInfo}, {30m, 21m, true, Decision.Manager}
    };
    [Theory] [MemberData(nameof(MealCases))]
    public void Meal_itemization_and_daily_aggregation(decimal amount, decimal prior, bool itemized, Decision expected)
    {
        var c = Fixtures.Claim(amount, Category.Meal) with { Receipt = Fixtures.Receipt(itemized) };
        Assert.Equal(expected, PolicyEngine.Evaluate(c, Policies.Demo, Fixtures.Facts with { PriorMealsBase = prior }).Decision);
    }

    public static TheoryData<decimal> InvalidAmounts => [-1m, 0m, 1000000.01m, 1.001m];
    [Theory] [MemberData(nameof(InvalidAmounts))]
    public void Invalid_money_is_rejected(decimal amount) => Assert.Equal(Decision.Reject,
        PolicyEngine.Evaluate(Fixtures.Claim(amount), Policies.Demo, Fixtures.Facts).Decision);

    [Theory]
    [InlineData(0, Decision.Approve)] [InlineData(90, Decision.Approve)]
    [InlineData(91, Decision.Reject)] [InlineData(-1, Decision.Reject)]
    public void Date_window_is_captured_input(int days, Decision expected) => Assert.Equal(expected,
        PolicyEngine.Evaluate(Fixtures.Claim() with { SpentOn = Fixtures.Today.AddDays(-days) }, Policies.Demo, Fixtures.Facts).Decision);

    public static TheoryData<string, string, decimal, int, string, Decision> FxCases => new()
    {
        {"EUR", "USD", 1.1m, 0, "treasury-demo-v1", Decision.MoreInfo},
        {"EUR", "USD", 1m, 0, "treasury-demo-v1", Decision.Approve},
        {"EUR", "USD", 1m, 1, "treasury-demo-v1", Decision.MoreInfo},
        {"EUR", "USD", 1m, 0, "unapproved", Decision.MoreInfo},
        {"GBP", "USD", 1m, 0, "treasury-demo-v1", Decision.MoreInfo},
        {"EUR", "GBP", 1m, 0, "treasury-demo-v1", Decision.MoreInfo},
        {"EUR", "USD", 0m, 0, "treasury-demo-v1", Decision.MoreInfo},
        {"EUR", "USD", -1m, 0, "treasury-demo-v1", Decision.MoreInfo}
    };
    [Theory] [MemberData(nameof(FxCases))]
    public void Fx_requires_exact_dated_provenance(string from, string to, decimal rate, int days, string source, Decision expected)
    {
        var c = Fixtures.Claim(25m) with { Amount = new(25m, "EUR") };
        var facts = Fixtures.Facts with { Rate = new ExchangeRate(from, to, rate, Fixtures.Today.AddDays(days), source) };
        Assert.Equal(expected, PolicyEngine.Evaluate(c, Policies.Demo, facts).Decision);
    }

    [Fact] public void Missing_fx_never_approves() => Assert.Equal(Decision.MoreInfo,
        PolicyEngine.Evaluate(Fixtures.Claim() with { Amount = new(20m, "GBP") }, Policies.Demo, Fixtures.Facts).Decision);
    [Fact] public void Fx_rounding_is_explicit() => Assert.Equal(20.01m,
        PolicyEngine.Evaluate(Fixtures.Claim() with { Amount = new(20m, "EUR") }, Policies.Demo,
            Fixtures.Facts with { Rate = new ExchangeRate("EUR", "USD", 1.00025m, Fixtures.Today, Policies.Demo.FxSource) }).BaseAmount);
    [Fact] public void Duplicate_has_precedence_over_missing_receipt() => Assert.Equal(Decision.Hold,
        PolicyEngine.Evaluate(Fixtures.Claim(500), Policies.Demo, Fixtures.Facts with { Duplicate = true }).Decision);
    [Fact] public void Invalid_policy_holds() => Assert.Equal(Decision.Hold,
        PolicyEngine.Evaluate(Fixtures.Claim(), Policies.Demo with { FinanceThreshold = 1 }, Fixtures.Facts).Decision);
    [Fact] public void Same_input_produces_same_serialized_evidence()
    {
        var c = Fixtures.Claim(240, Category.Meal) with { Receipt = Fixtures.Receipt() };
        var a = PolicyEngine.Evaluate(c, Policies.Demo, Fixtures.Facts);
        var b = PolicyEngine.Evaluate(c, Policies.Demo, Fixtures.Facts);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(a), System.Text.Json.JsonSerializer.Serialize(b));
        Assert.Equal("expense-v2", a.PolicyVersion);
        Assert.NotEmpty(a.Evidence);
    }
}
