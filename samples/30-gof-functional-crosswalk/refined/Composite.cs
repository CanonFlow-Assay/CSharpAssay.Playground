using System.Collections.Immutable;

namespace Playground.Gof.Refined;

public abstract record MenuNode;

public sealed record MenuItem(decimal Price) : MenuNode;

public sealed record MenuGroup(ImmutableArray<MenuNode> Children) : MenuNode;

public static class Menus
{
    public static decimal Total(MenuNode node) => node switch
    {
        MenuItem item => item.Price,
        MenuGroup group => group.Children.Sum(Total),
        _ => throw new ArgumentOutOfRangeException(nameof(node))
    };
}
