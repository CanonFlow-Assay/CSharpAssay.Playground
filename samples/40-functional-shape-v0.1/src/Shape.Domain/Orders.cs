using System.Collections.Immutable;

namespace Shape.Domain;

public sealed record OrderSubmission(
    Guid OrderId,
    ImmutableArray<OrderLineInput> Lines,
    Option<string> CustomerNote);

public sealed record OrderLineInput(string ProductCode, int Quantity);

public readonly record struct OrderId(Guid Value);

public sealed record ProductCode
{
    private ProductCode(string value) => Value = value;

    public string Value { get; }

    internal static Result<ProductCode, OrderError> Create(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? new Result<ProductCode, OrderError>.Failure(
                new OrderError.InvalidProductCode())
            : new Result<ProductCode, OrderError>.Success(
                new ProductCode(value.Trim()));
}

public readonly record struct Quantity
{
    private Quantity(int value) => Value = value;

    public int Value { get; }

    internal static Result<Quantity, OrderError> Create(int value) =>
        value <= 0
            ? new Result<Quantity, OrderError>.Failure(
                new OrderError.InvalidQuantity(value))
            : new Result<Quantity, OrderError>.Success(new Quantity(value));
}

public sealed record CustomerNote
{
    private CustomerNote(string value) => Value = value;

    public string Value { get; }

    internal static Result<CustomerNote, OrderError> Create(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? new Result<CustomerNote, OrderError>.Failure(
                new OrderError.InvalidCustomerNote())
            : new Result<CustomerNote, OrderError>.Success(
                new CustomerNote(value.Trim()));
}

public sealed record OrderLine(ProductCode ProductCode, Quantity Quantity);

public sealed record AcceptedOrder(
    OrderId OrderId,
    ImmutableArray<OrderLine> Lines,
    Option<CustomerNote> CustomerNote);

public abstract record OrderError
{
    private OrderError()
    {
    }

    public sealed record EmptyOrder : OrderError;

    public sealed record InvalidOrderId : OrderError;

    public sealed record InvalidProductCode : OrderError;

    public sealed record InvalidQuantity(int Attempted) : OrderError;

    public sealed record InvalidCustomerNote : OrderError;
}
