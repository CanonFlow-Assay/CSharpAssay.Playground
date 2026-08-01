namespace Playground.Gof.Classic;

public interface IMenuNode
{
    decimal Total();
}

public sealed class MenuItem(decimal price) : IMenuNode
{
    public decimal Total() => price;
}

public sealed class MenuGroup : IMenuNode
{
    public List<IMenuNode> Children { get; } = [];

    public decimal Total() => Children.Sum(child => child.Total());
}
