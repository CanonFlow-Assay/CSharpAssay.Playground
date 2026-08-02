using System.Collections.Immutable;
using Shape.Application;
using Shape.Domain;

namespace Shape.Infrastructure;

public sealed class InMemoryOrderStore : IOrderStore
{
    private readonly Lock _gate = new();
    private readonly List<AcceptedOrder> _orders = [];

    public Task SaveAsync(
        AcceptedOrder order,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _orders.Add(order);
        }

        return Task.CompletedTask;
    }

    public ImmutableArray<AcceptedOrder> Snapshot()
    {
        lock (_gate)
        {
            return [.. _orders];
        }
    }
}
