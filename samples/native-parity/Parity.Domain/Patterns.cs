using System.Collections.Immutable;

namespace Playground.Parity.Domain;

public enum InputError { EmptyCustomer, InvalidQuantity }
public sealed record PricedOrder(decimal Price, ImmutableArray<string> Audit);
public sealed record InvalidOrder(ImmutableArray<InputError> Errors);
public readonly union PricingDecision(PricedOrder, InvalidOrder);

public readonly record struct InvalidQuantity;
public readonly union QuantityValidation(Quantity, InvalidQuantity);

public sealed record Quantity
{
    private Quantity(int value) => Value = value;
    public int Value { get; }

    public static QuantityValidation Create(int value) => value is >= 1 and <= 99
        ? new Quantity(value)
        : new InvalidQuantity();
}

public static class Pricing
{
    public static PricingDecision Execute(Func<Quantity, decimal> price, string customer, int quantity)
    {
        var validQuantity = Quantity.Create(quantity);
        var errors = ImmutableArray<InputError>.Empty;
        if (string.IsNullOrWhiteSpace(customer))
        {
            errors = errors.Add(InputError.EmptyCustomer);
        }
        if (validQuantity is not Quantity accepted)
        {
            return new InvalidOrder(errors.Add(InputError.InvalidQuantity));
        }
        if (!errors.IsEmpty)
        {
            return new InvalidOrder(errors);
        }
        return new PricedOrder(price(accepted), ["input-validated", "price-calculated"]);
    }
}

public readonly record struct Passed(int Count);
public readonly record struct Failed(int Count);
public sealed record InvalidExecution(string Reason);
public sealed record Unavailable(string Reason);
public readonly union TestExecution(Passed, Failed, InvalidExecution, Unavailable);
public sealed record Probe(string Id, string RequirementId);
public readonly record struct NoProbe;
public readonly union ProbeAvailability(NoProbe, Probe);
public sealed record Observation(TestExecution Baseline, TestExecution Candidate, bool SnapshotMatches,
    int RemainingBudget, ProbeAvailability RegisteredProbe);

public sealed record RepairBaseline(string Reason);
public sealed record RepairCandidate(string Reason);
public sealed record ContextRequired(string Reason);
public readonly record struct BudgetExhausted;
public sealed record ProbeRequested(Probe Probe);
public readonly record struct SurvivorReview;
public readonly record struct NoInvestigation;
public readonly union Advice(RepairBaseline, RepairCandidate, ContextRequired, BudgetExhausted,
    ProbeRequested, SurvivorReview, NoInvestigation);

public static class DecisionAssist
{
    public static Advice Decide(Observation observation) => !observation.SnapshotMatches
        ? new ContextRequired("source snapshot changed")
        : observation.Baseline switch
        {
            Failed => new RepairBaseline("original tests failed"),
            InvalidExecution invalid => new RepairBaseline(invalid.Reason),
            Unavailable unavailable => new ContextRequired(unavailable.Reason),
            Passed { Count: <= 0 } => new ContextRequired("baseline contains no tests"),
            Passed => DecideCandidate(observation.Candidate, observation.RegisteredProbe, observation.RemainingBudget)
        };

    private static Advice DecideCandidate(TestExecution candidate, ProbeAvailability probe, int budget) =>
        candidate switch
        {
            Unavailable unavailable => new ContextRequired(unavailable.Reason),
            InvalidExecution invalid => new RepairCandidate(invalid.Reason),
            Failed { Count: <= 0 } => new ContextRequired("invalid failure count"),
            Failed => new NoInvestigation(),
            Passed { Count: <= 0 } => new ContextRequired("candidate contains no tests"),
            Passed => probe switch
            {
                NoProbe => new SurvivorReview(),
                Probe when budget <= 0 => new BudgetExhausted(),
                Probe registered => new ProbeRequested(registered)
            }
        };
}
