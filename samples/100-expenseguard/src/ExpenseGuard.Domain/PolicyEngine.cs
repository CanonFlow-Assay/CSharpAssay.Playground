using System.Collections.Immutable;

namespace ExpenseGuard.Domain;

public static class PolicyEngine
{
    public static Assessment Evaluate(ExpenseClaim claim, ExpensePolicy policy, PolicyFacts facts)
    {
        var evidence = ImmutableArray<RuleEvidence>.Empty;
        var rate = 0m;
        if (!ValidPolicy(policy))
            return Result(Decision.Hold, 0m, "none", "POLICY", "InvalidPolicy", "Policy configuration is invalid.");
        if (claim.Amount.Value is <= 0m or > 1000000m || decimal.Round(claim.Amount.Value, 2) != claim.Amount.Value ||
            claim.Amount.Currency is not ("USD" or "EUR" or "GBP") || !Enum.IsDefined(claim.Category) ||
            claim.Units is < 1 or > 366 || string.IsNullOrWhiteSpace(claim.Merchant))
            return Result(Decision.Reject, 0m, "none", "VALIDATION", "InvalidClaim", "Invalid amount, currency, category, units or merchant.");
        if (claim.SpentOn > facts.Today || facts.Today.DayNumber - claim.SpentOn.DayNumber > policy.MaximumAgeDays)
            return Result(Decision.Reject, 0m, "none", "DATE", "OutsideWindow", "Expense date is outside the policy window.");
        if (claim.Category == Category.Personal)
            return Result(Decision.Reject, 0m, "none", "CATEGORY", "PersonalExpense", "Personal expenses are ineligible.");
        if (facts.Duplicate)
            return Result(Decision.Hold, 0m, "none", "DUPLICATE", "PossibleDuplicate", "Receipt hash or merchant/amount/date matches another claim.");
        if (facts.PriorMealsBase < 0m)
            return Result(Decision.Hold, 0m, "none", "LEDGER", "InvalidLedger", "Prior daily meal total is invalid.");

        rate = claim.Amount.Currency == policy.BaseCurrency ? 1m : facts.Rate switch
        {
            ExchangeRate r when r.From == claim.Amount.Currency && r.To == policy.BaseCurrency &&
                r.On == claim.SpentOn && r.Source == policy.FxSource && r.Rate is > 0m and <= 1000m => r.Rate,
            _ => 0m
        };
        if (rate == 0m)
            return Result(Decision.MoreInfo, 0m, "unavailable", "FX", "RateUnavailable", "A dated policy-approved FX rate is required.");
        var amount = decimal.Round(claim.Amount.Value * rate, 2, MidpointRounding.AwayFromZero);
        var source = claim.Amount.Currency == policy.BaseCurrency ? "identity" : policy.FxSource;
        evidence = evidence.Add(new("FX", "Normalized", "Amount normalized using the declared dated rate; midpoint rounded away from zero."));
        if (amount > policy.ReceiptThreshold && claim.Receipt is MissingReceipt)
            return Result(Decision.MoreInfo, amount, source, "RECEIPT", "ReceiptRequired", "Receipt required above the base-currency threshold.");
        if (claim.Category == Category.Meal && amount + facts.PriorMealsBase > policy.MealDailyLimit &&
            claim.Receipt is not Receipt { Itemized: true })
            return Result(Decision.MoreInfo, amount, source, "MEAL", "ItemizedRequired", "Daily meals above the limit require an itemized receipt.");

        var categoryApproval = claim.Category switch
        {
            Category.Meal => amount + facts.PriorMealsBase > policy.MealDailyLimit,
            Category.Transport => amount > policy.TransportLimit,
            Category.Hotel => amount / claim.Units > policy.HotelNightLimit,
            Category.Software => amount / claim.Units > policy.SoftwareMonthLimit,
            Category.Other => false,
            Category.Personal => false,
            _ => true
        };
        if (amount > policy.FinanceThreshold)
            return Result(Decision.ManagerThenFinance, amount, source, "APPROVAL", "FinanceThreshold", "Manager and distinct finance approval required.");
        if (amount > policy.ManagerThreshold || categoryApproval)
            return Result(Decision.Manager, amount, source, "APPROVAL", "ManagerRequired", "Amount or category limit requires manager approval.");
        return Result(Decision.Approve, amount, source, "APPROVAL", "WithinPolicy", "All configured checks passed.");

        Assessment Result(Decision decision, decimal normalized, string fx, string rule, string code, string detail) =>
            new(policy.Version, facts.Today, normalized, policy.BaseCurrency, fx, rate, facts.PriorMealsBase, facts.Duplicate, decision,
                evidence.Add(new(rule, code, detail)));
    }

    public static bool ValidPolicy(ExpensePolicy p) => !string.IsNullOrWhiteSpace(p.Version) &&
        p.BaseCurrency is "USD" or "EUR" or "GBP" && !string.IsNullOrWhiteSpace(p.FxSource) &&
        p.ReceiptThreshold >= 0 && p.MealDailyLimit > 0 && p.TransportLimit > 0 &&
        p.HotelNightLimit > 0 && p.SoftwareMonthLimit > 0 && p.ManagerThreshold >= p.ReceiptThreshold &&
        p.FinanceThreshold > p.ManagerThreshold && p.MaximumAgeDays > 0;
}
