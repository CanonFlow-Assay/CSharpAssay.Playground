using System.Collections.Immutable;
using System.Diagnostics;

namespace Shape.Domain;

public static class OrderDecisions
{
    private const int MaximumLineCount = 5;

    public static Result<AcceptedOrder, OrderError> Accept(
        OrderSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (submission.OrderId == Guid.Empty)
        {
            return new Result<AcceptedOrder, OrderError>.Failure(
                new OrderError.InvalidOrderId());
        }

        if (submission.Lines.IsDefaultOrEmpty)
        {
            return new Result<AcceptedOrder, OrderError>.Failure(
                new OrderError.EmptyOrder());
        }

        if (submission.Lines.Length > MaximumLineCount)
        {
            return new Result<AcceptedOrder, OrderError>.Failure(
                new OrderError.TooManyLines(
                    MaximumLineCount,
                    submission.Lines.Length));
        }

        var lines = ImmutableArray.CreateBuilder<OrderLine>(
            submission.Lines.Length);
        foreach (var input in submission.Lines)
        {
            var productCode = ProductCode.Create(input.ProductCode);
            if (productCode is Result<ProductCode, OrderError>.Failure codeFailure)
            {
                return new Result<AcceptedOrder, OrderError>.Failure(
                    codeFailure.Error);
            }

            var quantity = Quantity.Create(input.Quantity);
            if (quantity is Result<Quantity, OrderError>.Failure quantityFailure)
            {
                return new Result<AcceptedOrder, OrderError>.Failure(
                    quantityFailure.Error);
            }

            lines.Add(new OrderLine(
                ((Result<ProductCode, OrderError>.Success)productCode).Value,
                ((Result<Quantity, OrderError>.Success)quantity).Value));
        }

        var note = ValidateNote(submission.CustomerNote);
        if (note is Result<Option<CustomerNote>, OrderError>.Failure noteFailure)
        {
            return new Result<AcceptedOrder, OrderError>.Failure(
                noteFailure.Error);
        }

        return new Result<AcceptedOrder, OrderError>.Success(
            new AcceptedOrder(
                new OrderId(submission.OrderId),
                lines.MoveToImmutable(),
                ((Result<Option<CustomerNote>, OrderError>.Success)note).Value));
    }

    private static Result<Option<CustomerNote>, OrderError> ValidateNote(
        Option<string> note) =>
        note switch
        {
            Option<string>.None =>
                new Result<Option<CustomerNote>, OrderError>.Success(
                    new Option<CustomerNote>.None()),
            Option<string>.Some some => CustomerNote.Create(some.Value) switch
            {
                Result<CustomerNote, OrderError>.Success success =>
                    new Result<Option<CustomerNote>, OrderError>.Success(
                        new Option<CustomerNote>.Some(success.Value)),
                Result<CustomerNote, OrderError>.Failure failure =>
                    new Result<Option<CustomerNote>, OrderError>.Failure(
                        failure.Error),
                _ => throw new UnreachableException(),
            },
            _ => throw new UnreachableException(),
        };
}
