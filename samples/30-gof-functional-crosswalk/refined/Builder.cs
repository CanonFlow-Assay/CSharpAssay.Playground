using System.Collections.Immutable;

namespace Playground.Gof.Refined;

public sealed record MealOrder(string Main, ImmutableArray<string> Extras);

public sealed record MealDraft(string Main, ImmutableArray<string> Extras)
{
    public static MealDraft Empty { get; } = new("House salad", []);
}

public static class MealOrders
{
    public static MealDraft WithMain(MealDraft draft, string value) =>
        draft with { Main = value };

    public static MealDraft AddExtra(MealDraft draft, string value) =>
        draft with { Extras = draft.Extras.Add(value) };

    public static MealOrder Build(MealDraft draft) => new(draft.Main, draft.Extras);
}
