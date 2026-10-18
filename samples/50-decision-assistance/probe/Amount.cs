namespace Playground.DecisionAssist.Probe;

public readonly union AmountDecision(ValidAmount, InvalidAmount);
public readonly record struct ValidAmount(int Value);
public readonly record struct InvalidAmount;

public static class AmountPolicy
{
    public static AmountDecision Validate(int amount) => amount > 0
        ? new ValidAmount(amount)
        : new InvalidAmount();
}
