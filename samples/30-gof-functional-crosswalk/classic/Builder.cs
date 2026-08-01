using System.Collections.Immutable;

namespace Playground.Gof.Classic;

public sealed record MealOrder(string Main, ImmutableArray<string> Extras);

public sealed class MealOrderBuilder
{
    private string main = "House salad";
    private ImmutableArray<string> extras = [];

    public MealOrderBuilder WithMain(string value)
    {
        main = value;
        return this;
    }

    public MealOrderBuilder AddExtra(string value)
    {
        extras = extras.Add(value);
        return this;
    }

    public MealOrder Build() => new(main, extras);
}
