using System.Collections.Immutable;
using Microsoft.AspNetCore.Http.HttpResults;
using Shape.Api;
using Shape.Application;
using Shape.Domain;
using Shape.Infrastructure;
using Xunit;

namespace Shape.Behavior.Tests;

public sealed class OrderWorkflowTests
{
    [Fact]
    public void Valid_submission_produces_an_accepted_order()
    {
        var result = OrderDecisions.Accept(ValidSubmission());

        var success = Assert.IsType<
            Result<AcceptedOrder, OrderError>.Success>(result);
        Assert.Single(success.Value.Lines);
        Assert.Equal("SKU-1", success.Value.Lines[0].ProductCode.Value);
    }

    [Fact]
    public void Empty_order_is_an_explicit_failure()
    {
        var submission = ValidSubmission() with
        {
            Lines = ImmutableArray<OrderLineInput>.Empty,
        };

        var result = OrderDecisions.Accept(submission);

        var failure = Assert.IsType<
            Result<AcceptedOrder, OrderError>.Failure>(result);
        Assert.IsType<OrderError.EmptyOrder>(failure.Error);
    }

    [Fact]
    public void Invalid_product_code_is_an_explicit_failure()
    {
        var submission = ValidSubmission() with
        {
            Lines = [new OrderLineInput(" ", 1)],
        };

        var result = OrderDecisions.Accept(submission);

        var failure = Assert.IsType<
            Result<AcceptedOrder, OrderError>.Failure>(result);
        Assert.IsType<OrderError.InvalidProductCode>(failure.Error);
    }

    [Fact]
    public void Optional_customer_note_preserves_some_case()
    {
        var result = OrderDecisions.Accept(ValidSubmission() with
        {
            CustomerNote = new Option<string>.Some("  leave at reception  "),
        });

        var success = Assert.IsType<
            Result<AcceptedOrder, OrderError>.Success>(result);
        var note = Assert.IsType<Option<CustomerNote>.Some>(
            success.Value.CustomerNote);
        Assert.Equal("leave at reception", note.Value.Value);
    }

    [Fact]
    public async Task Successful_workflow_performs_exactly_one_effect()
    {
        var store = new InMemoryOrderStore();
        var handler = new SubmitOrderHandler(store);

        var result = await handler.HandleAsync(
            ValidSubmission(),
            TestContext.Current.CancellationToken);

        Assert.IsType<Result<OrderReceipt, OrderError>.Success>(result);
        Assert.Single(store.Snapshot());
    }

    [Fact]
    public async Task Domain_failure_performs_no_effect()
    {
        var store = new InMemoryOrderStore();
        var handler = new SubmitOrderHandler(store);
        var invalid = ValidSubmission() with
        {
            Lines = ImmutableArray<OrderLineInput>.Empty,
        };

        var result = await handler.HandleAsync(
            invalid,
            TestContext.Current.CancellationToken);

        Assert.IsType<Result<OrderReceipt, OrderError>.Failure>(result);
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public async Task Five_line_order_is_accepted()
    {
        var store = new InMemoryOrderStore();
        var handler = new SubmitOrderHandler(store);
        var submission = ValidSubmission() with
        {
            Lines = CreateLines(5),
        };

        var result = await handler.HandleAsync(
            submission,
            TestContext.Current.CancellationToken);

        var success = Assert.IsType<
            Result<OrderReceipt, OrderError>.Success>(result);
        Assert.Equal(5, success.Value.StoredLineCount);
        Assert.Single(store.Snapshot());
        Assert.Equal(5, store.Snapshot()[0].Lines.Length);
    }

    [Fact]
    public async Task Six_line_order_is_rejected_without_storage_effects()
    {
        var store = new InMemoryOrderStore();
        var handler = new SubmitOrderHandler(store);
        var submission = ValidSubmission() with
        {
            Lines = CreateLines(6),
        };

        var result = await handler.HandleAsync(
            submission,
            TestContext.Current.CancellationToken);

        var failure = Assert.IsType<
            Result<OrderReceipt, OrderError>.Failure>(result);
        var error = Assert.IsType<OrderError.TooManyLines>(failure.Error);
        Assert.Equal(5, error.Maximum);
        Assert.Equal(6, error.Actual);
        Assert.Empty(store.Snapshot());

        var response = OrderEndpoint.ToResponse(result);
        var unprocessable = Assert.IsType<
            UnprocessableEntity<OrderErrorResponse>>(response.Result);
        Assert.Equal("too_many_order_lines", unprocessable.Value?.Code);
    }

    [Fact]
    public void Api_boundary_maps_null_note_to_none()
    {
        var request = new OrderRequest(
            Guid.NewGuid(),
            [new OrderLineRequest("SKU-1", 1)],
            null);

        var submission = OrderEndpoint.ToSubmission(request);

        Assert.IsType<Option<string>.None>(submission.CustomerNote);
    }

    [Fact]
    public void Api_boundary_maps_domain_failure_to_422()
    {
        var failure = new Result<OrderReceipt, OrderError>.Failure(
            new OrderError.EmptyOrder());

        var response = OrderEndpoint.ToResponse(failure);

        var unprocessable = Assert.IsType<
            UnprocessableEntity<OrderErrorResponse>>(response.Result);
        Assert.Equal("empty_order", unprocessable.Value?.Code);
    }

    private static OrderSubmission ValidSubmission() =>
        new(
            Guid.NewGuid(),
            [new OrderLineInput("SKU-1", 2)],
            new Option<string>.None());

    private static ImmutableArray<OrderLineInput> CreateLines(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new OrderLineInput($"SKU-{index}", 1))
            .ToImmutableArray();
}
