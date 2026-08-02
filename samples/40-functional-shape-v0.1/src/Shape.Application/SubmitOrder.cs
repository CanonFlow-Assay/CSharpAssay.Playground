using System.Diagnostics;
using Shape.Domain;

namespace Shape.Application;

public interface IOrderStore
{
    Task SaveAsync(AcceptedOrder order, CancellationToken cancellationToken);
}

public sealed record OrderReceipt(OrderId OrderId, int StoredLineCount);

public sealed class SubmitOrderHandler(IOrderStore orderStore)
{
    public async Task<Result<OrderReceipt, OrderError>> HandleAsync(
        OrderSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var decision = OrderDecisions.Accept(submission);
        switch (decision)
        {
            case Result<AcceptedOrder, OrderError>.Failure failure:
                return new Result<OrderReceipt, OrderError>.Failure(
                    failure.Error);
            case Result<AcceptedOrder, OrderError>.Success success:
                await orderStore.SaveAsync(success.Value, cancellationToken)
                    .ConfigureAwait(false);
                return new Result<OrderReceipt, OrderError>.Success(
                    new OrderReceipt(
                        success.Value.OrderId,
                        success.Value.Lines.Length));
            default:
                throw new UnreachableException();
        }
    }
}
