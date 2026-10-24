namespace EfBoundary.Domain;

public sealed record OrderId(int Value);
public sealed record OrderedQuantity(int Value);
public sealed record NoMemo;
public sealed record MemoText(string Value);
public readonly union OrderMemo(NoMemo, MemoText);
public sealed record OrderValue(OrderId Id, OrderedQuantity Quantity, OrderMemo Memo);
public sealed record StoredOrder(OrderValue Value);
public sealed record MissingOrder(OrderId Id);
public sealed record InvalidOrder(string Reason);
public readonly union OrderOutcome(StoredOrder, MissingOrder, InvalidOrder);

public static class OrderDecisions
{
    public static OrderOutcome FromPersisted(int id, int quantity, OrderMemo memo) => (id, quantity) switch
    {
        (<= 0, _) => new InvalidOrder("Order identity must be positive."),
        (_, <= 0) => new InvalidOrder("Quantity must be positive."),
        _ => new StoredOrder(new(new(id), new(quantity), memo))
    };
}
