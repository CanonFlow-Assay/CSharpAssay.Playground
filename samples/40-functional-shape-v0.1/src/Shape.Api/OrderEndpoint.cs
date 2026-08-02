using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Shape.Application;
using Shape.Domain;

namespace Shape.Api;

public sealed record OrderRequest(
    Guid OrderId,
    IReadOnlyList<OrderLineRequest> Lines,
    string? CustomerNote);

public sealed record OrderLineRequest(string ProductCode, int Quantity);

public sealed record OrderResponse(Guid OrderId, int StoredLineCount);

public sealed record OrderErrorResponse(string Code);

public static class OrderEndpoint
{
    public static OrderSubmission ToSubmission(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var note = string.IsNullOrWhiteSpace(request.CustomerNote)
            ? (Option<string>)new Option<string>.None()
            : new Option<string>.Some(request.CustomerNote);

        return new OrderSubmission(
            request.OrderId,
            request.Lines
                .Select(line => new OrderLineInput(
                    line.ProductCode,
                    line.Quantity))
                .ToImmutableArray(),
            note);
    }

    public static Results<Created<OrderResponse>, UnprocessableEntity<OrderErrorResponse>>
        ToResponse(Result<OrderReceipt, OrderError> result) =>
        result switch
        {
            Result<OrderReceipt, OrderError>.Success success =>
                TypedResults.Created(
                    $"/orders/{success.Value.OrderId.Value}",
                    new OrderResponse(
                        success.Value.OrderId.Value,
                        success.Value.StoredLineCount)),
            Result<OrderReceipt, OrderError>.Failure failure =>
                TypedResults.UnprocessableEntity(
                    new OrderErrorResponse(ErrorCode(failure.Error))),
            _ => throw new UnreachableException(),
        };

    private static string ErrorCode(OrderError error) =>
        error switch
        {
            OrderError.EmptyOrder => "empty_order",
            OrderError.TooManyLines => "too_many_order_lines",
            OrderError.InvalidOrderId => "invalid_order_id",
            OrderError.InvalidProductCode => "invalid_product_code",
            OrderError.InvalidQuantity => "invalid_quantity",
            OrderError.InvalidCustomerNote => "invalid_customer_note",
            _ => throw new UnreachableException(),
        };
}
