using System.Collections.Immutable;

namespace Playground.Parity.Domain;

public enum DebitProblem { InvalidReference, InvalidAmount, Precision, LimitExceeded }
public readonly record struct InvalidDebit(DebitProblem Problem);
public readonly union DebitValidation(Debit, InvalidDebit);

public sealed record Debit
{
    private Debit(string reference, decimal amount) => (Reference, Amount) = (reference, amount);
    public string Reference { get; }
    public decimal Amount { get; }

    public static DebitValidation Create(decimal limit, string reference, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 64)
        {
            return new InvalidDebit(DebitProblem.InvalidReference);
        }
        if (amount is <= 0m or > 999_999_999_999.99m)
        {
            return new InvalidDebit(DebitProblem.InvalidAmount);
        }
        if (decimal.Round(amount, 2) != amount)
        {
            return new InvalidDebit(DebitProblem.Precision);
        }
        return amount > limit ? new InvalidDebit(DebitProblem.LimitExceeded) : new Debit(reference.Trim(), amount);
    }
}

public sealed record AcceptedDebit(decimal Balance);
public readonly record struct InsufficientFunds;
public readonly record struct DebitConflict;
public readonly union DebitOutcome(AcceptedDebit, InsufficientFunds, DebitConflict);

public sealed record DebitOperation(string Wallet, decimal Amount, DebitOutcome Outcome);
public sealed record WalletState(ImmutableDictionary<string, decimal> Balances,
    ImmutableDictionary<string, DebitOperation> Operations)
{
    public static WalletState Start(string wallet, decimal balance) =>
        new(ImmutableDictionary<string, decimal>.Empty.Add(wallet, balance),
            ImmutableDictionary<string, DebitOperation>.Empty);
}

public static class Wallet
{
    public static (WalletState State, DebitOutcome Outcome) Apply(WalletState state, string wallet, Debit debit)
    {
        if (state.Operations.TryGetValue(debit.Reference, out var previous))
        {
            return (state, previous.Wallet == wallet && previous.Amount == debit.Amount
                ? previous.Outcome : new DebitConflict());
        }

        var hasFunds = state.Balances.TryGetValue(wallet, out var balance) && balance >= debit.Amount;
        var remaining = balance - debit.Amount;
        DebitOutcome outcome = hasFunds ? new AcceptedDebit(remaining) : new InsufficientFunds();
        return (state with
        {
            Balances = hasFunds ? state.Balances.SetItem(wallet, remaining) : state.Balances,
            Operations = state.Operations.Add(debit.Reference, new(wallet, debit.Amount, outcome))
        }, outcome);
    }
}
