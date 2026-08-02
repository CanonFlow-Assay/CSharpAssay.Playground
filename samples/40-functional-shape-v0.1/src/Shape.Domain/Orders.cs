using System.Collections.Immutable;

namespace Shape.Domain;

public sealed record OrderSubmission(
    Guid OrderId,
    ImmutableArray<OrderLineInput> Lines,
    Option<string> CustomerNote);

public sealed record OrderLineInput(string ProductCode, int Quantity);

public sealed record OrderId
{
    private OrderId(Guid value) => Value = value;

    public Guid Value { get; }

    internal static Result<OrderId, OrderError> Create(Guid value) =>
        value == Guid.Empty
            ? new Result<OrderId, OrderError>.Failure(
                new OrderError.InvalidOrderId())
            : new Result<OrderId, OrderError>.Success(new OrderId(value));
}

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

public sealed record Quantity
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

public sealed record OrderLine
{
    internal OrderLine(ProductCode productCode, Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(productCode);
        ArgumentNullException.ThrowIfNull(quantity);
        ProductCode = productCode;
        Quantity = quantity;
    }

    public ProductCode ProductCode { get; }

    public Quantity Quantity { get; }
}

public sealed record AcceptedOrder
{
    internal AcceptedOrder(
        OrderId orderId,
        ImmutableArray<OrderLine> lines,
        Option<CustomerNote> customerNote)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(customerNote);
        if (lines.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "An accepted order must contain validated lines.",
                nameof(lines));
        }

        OrderId = orderId;
        Lines = lines;
        CustomerNote = customerNote;
    }

    public OrderId OrderId { get; }

    public ImmutableArray<OrderLine> Lines { get; }

    public Option<CustomerNote> CustomerNote { get; }
}

public abstract record OrderError
{
    private OrderError()
    {
    }

    public sealed record EmptyOrder : OrderError;

    public sealed record TooManyLines(int Maximum, int Actual) : OrderError;

    public sealed record InvalidOrderId : OrderError;

    public sealed record InvalidProductCode : OrderError;

    public sealed record InvalidQuantity(int Attempted) : OrderError;

    public sealed record InvalidCustomerNote : OrderError;
}
