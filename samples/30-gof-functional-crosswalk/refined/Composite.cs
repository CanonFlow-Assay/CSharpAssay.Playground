using System.Collections.Immutable;

namespace Playground.Gof.Refined;

public readonly union MenuNode(MenuItem, MenuGroup);

public sealed record MenuItem(decimal Price);

public sealed record MenuGroup(ImmutableArray<MenuNode> Children);

public static class Menus
{
    public static decimal Total(MenuNode node) => node switch
    {
        MenuItem item => item.Price,
        MenuGroup group => group.Children.Sum(Total)
    };
}
