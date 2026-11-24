using System.Collections.Immutable;

namespace RulesDsl;

public static class Program
{
    public static int Main(string[] args) => args switch
    {
        ["--verify"] => Verify(),
        _ => Usage()
    };

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: RulesDsl --verify");
        return 64;
    }

    private static int Verify()
    {
        var cases = new (string Name, string Text, Applicant Applicant, string Expected)[]
        {
            ("match", "country=IN;minimum=100;category=software", new Applicant("IN", "software", 120), "eligible"),
            ("amount-fails", "country=IN;minimum=100", new Applicant("IN", "software", 99), "ineligible|amount-below-minimum:100"),
            ("country-fails", "country=IN;minimum=100", new Applicant("US", "software", 120), "ineligible|country-mismatch:IN"),
            ("multiple-reasons", "country=IN;minimum=100;category=software", new Applicant("US", "hardware", 0), "ineligible|country-mismatch:IN,amount-below-minimum:100,category-mismatch:software"),
            ("unknown-clause", "country=IN;priority=gold", new Applicant("IN", "software", 120), "parse-rejected|unknown-clause:priority"),
            ("malformed-clause", "country", new Applicant("IN", "software", 120), "parse-rejected|malformed-clause:country")
        };

        foreach (var test in cases)
        {
            var parsed = RuleText.Parse(test.Text);
            var actual = Rules.Canonical(parsed, test.Applicant);
            if (!string.Equals(actual, test.Expected, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"{test.Name}: expected {test.Expected}, got {actual}.");
                return 1;
            }
        }

        Console.WriteLine($"Rules DSL: {cases.Length} cases passed.");
        return 0;
    }
}

public sealed record Applicant(string Country, string Category, int Amount);
public sealed record CountryIs(string Value);
public sealed record CategoryIs(string Value);
public sealed record MinimumAmount(int Value);
public sealed record AllRules(ImmutableArray<EligibilityRule> Values);
public readonly union EligibilityRule(CountryIs, CategoryIs, MinimumAmount, AllRules);
public sealed record ParseAccepted(EligibilityRule Rule);
public sealed record ParseRejected(ImmutableArray<RuleIssue> Issues);
public readonly union RuleParse(ParseAccepted, ParseRejected);
public sealed record RuleIssue(string Code, string Detail);
public sealed record Eligible;
public sealed record Ineligible(ImmutableArray<RuleIssue> Reasons);
public readonly union EligibilityDecision(Eligible, Ineligible);

public static class RuleText
{
    public static RuleParse Parse(string text)
    {
        var rules = ImmutableArray.CreateBuilder<EligibilityRule>();
        var issues = ImmutableArray.CreateBuilder<RuleIssue>();
        foreach (var clause in text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = clause.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[1]))
            {
                issues.Add(new RuleIssue("malformed-clause", clause));
                continue;
            }
            AddClause(pair[0], pair[1], rules, issues);
        }
        return issues.Count == 0 ? new ParseAccepted(new AllRules(rules.ToImmutable())) : new ParseRejected(issues.ToImmutable());
    }

    private static void AddClause(string name, string value, ImmutableArray<EligibilityRule>.Builder rules, ImmutableArray<RuleIssue>.Builder issues)
    {
        switch (name)
        {
            case "country": rules.Add(new CountryIs(value)); break;
            case "category": rules.Add(new CategoryIs(value)); break;
            case "minimum" when int.TryParse(value, out var amount) && amount >= 0: rules.Add(new MinimumAmount(amount)); break;
            case "minimum": issues.Add(new RuleIssue("invalid-minimum", value)); break;
            default: issues.Add(new RuleIssue("unknown-clause", name)); break;
        }
    }
}

public static class Rules
{
    public static string Canonical(RuleParse parsed, Applicant applicant) => parsed switch
    {
        ParseAccepted accepted => Canonical(Evaluate(accepted.Rule, applicant)),
        ParseRejected rejected => $"parse-rejected|{string.Join(',', rejected.Issues.Select(issue => $"{issue.Code}:{issue.Detail}"))}"
    };

    private static EligibilityDecision Evaluate(EligibilityRule rule, Applicant applicant) => rule switch
    {
        CountryIs country when applicant.Country == country.Value => new Eligible(),
        CountryIs country => new Ineligible([new RuleIssue("country-mismatch", country.Value)]),
        CategoryIs category when applicant.Category == category.Value => new Eligible(),
        CategoryIs category => new Ineligible([new RuleIssue("category-mismatch", category.Value)]),
        MinimumAmount minimum when applicant.Amount >= minimum.Value => new Eligible(),
        MinimumAmount minimum => new Ineligible([new RuleIssue("amount-below-minimum", minimum.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))]),
        AllRules all => Combine(all.Values.Select(child => Evaluate(child, applicant)))
    };

    private static EligibilityDecision Combine(IEnumerable<EligibilityDecision> decisions)
    {
        var reasons = ImmutableArray.CreateBuilder<RuleIssue>();
        foreach (var decision in decisions)
        {
            switch (decision)
            {
                case Eligible: break;
                case Ineligible rejected: reasons.AddRange(rejected.Reasons); break;
            }
        }
        return reasons.Count == 0 ? new Eligible() : new Ineligible(reasons.ToImmutable());
    }

    private static string Canonical(EligibilityDecision decision) => decision switch
    {
        Eligible => "eligible",
        Ineligible rejected => $"ineligible|{string.Join(',', rejected.Reasons.Select(reason => $"{reason.Code}:{reason.Detail}"))}"
    };
}
