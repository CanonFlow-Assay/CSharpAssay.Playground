using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Shape.Api;
using Shape.Application;
using Shape.Domain;
using Shape.Infrastructure;
using Xunit;

namespace Shape.Behavior.Tests;

// Additional requirements are tested separately so OrderWorkflowTests remains
// the unchanged before baseline for coverage and mutation comparisons.
public sealed class OrderCoverageTests
{
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Nonpositive_quantity_is_rejected_without_effects(int quantity)
    {
        var store = new RecordingStore();
        var result = await new SubmitOrderHandler(store).HandleAsync(
            Submission(quantity: quantity), TestContext.Current.CancellationToken);
        var error = Assert.IsType<OrderError.InvalidQuantity>(Failure(result));
        Assert.Equal(quantity, error.Attempted);
        Assert.Equal(0, store.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void Positive_quantity_preserves_the_exact_value(int quantity)
    {
        var accepted = Accepted(Submission(quantity: quantity));
        Assert.Equal(quantity, Assert.Single(accepted.Lines).Quantity.Value);
    }

    [Fact]
    public async Task Empty_order_id_is_rejected_without_effects()
    {
        var store = new RecordingStore();
        var result = await new SubmitOrderHandler(store).HandleAsync(
            Submission() with { OrderId = Guid.Empty }, TestContext.Current.CancellationToken);
        Assert.IsType<OrderError.InvalidOrderId>(Failure(result));
        Assert.Equal(0, store.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Allowed_line_counts_preserve_all_lines_and_their_order(int count)
    {
        var lines = Enumerable.Range(1, count).Select(index => new OrderLineInput($"SKU-{index}", index)).ToImmutableArray();
        var accepted = Accepted(Submission() with { Lines = lines });
        Assert.Equal(count, accepted.Lines.Length);
        Assert.Equal(lines.Select(line => line.ProductCode), accepted.Lines.Select(line => line.ProductCode.Value));
        Assert.Equal(lines.Select(line => line.Quantity), accepted.Lines.Select(line => line.Quantity.Value));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(20)]
    public void Excess_lines_report_the_exact_limit_and_actual_count(int count)
    {
        var lines = Enumerable.Range(1, count).Select(index => new OrderLineInput($"SKU-{index}", 1)).ToImmutableArray();
        var result = OrderDecisions.Accept(Submission() with { Lines = lines });
        var error = Assert.IsType<OrderError.TooManyLines>(Assert.IsType<Result<AcceptedOrder, OrderError>.Failure>(result).Error);
        Assert.Equal(5, error.Maximum);
        Assert.Equal(count, error.Actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void Whitespace_product_codes_are_rejected(string code)
    {
        var result = OrderDecisions.Accept(Submission(code: code));
        Assert.IsType<OrderError.InvalidProductCode>(Assert.IsType<Result<AcceptedOrder, OrderError>.Failure>(result).Error);
    }

    [Theory]
    [InlineData(" SKU-1 ")]
    [InlineData("\tSKU-1\r\n")]
    public void Product_code_is_trimmed_once_at_domain_validation(string code) =>
        Assert.Equal("SKU-1", Assert.Single(Accepted(Submission(code: code)).Lines).ProductCode.Value);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Explicit_blank_domain_note_is_rejected(string note)
    {
        var result = OrderDecisions.Accept(Submission() with { CustomerNote = new Option<string>.Some(note) });
        Assert.IsType<OrderError.InvalidCustomerNote>(Assert.IsType<Result<AcceptedOrder, OrderError>.Failure>(result).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Blank_transport_note_becomes_none(string note)
    {
        var submission = OrderEndpoint.ToSubmission(new OrderRequest(Guid.NewGuid(), [new OrderLineRequest("SKU-1", 1)], note));
        Assert.IsType<Option<string>.None>(submission.CustomerNote);
        Assert.IsType<Option<CustomerNote>.None>(Accepted(submission).CustomerNote);
    }

    [Fact]
    public void Transport_and_domain_preserve_and_normalize_a_nonblank_note()
    {
        var submission = OrderEndpoint.ToSubmission(new OrderRequest(Guid.NewGuid(), [new OrderLineRequest(" SKU-1 ", 3)], "  reception  "));
        Assert.Equal("  reception  ", Assert.IsType<Option<string>.Some>(submission.CustomerNote).Value);
        var accepted = Accepted(submission);
        Assert.Equal("reception", Assert.IsType<Option<CustomerNote>.Some>(accepted.CustomerNote).Value.Value);
        Assert.Equal("SKU-1", Assert.Single(accepted.Lines).ProductCode.Value);
        Assert.Equal(3, Assert.Single(accepted.Lines).Quantity.Value);
    }

    [Theory]
    [InlineData("empty_order")]
    [InlineData("too_many_order_lines")]
    [InlineData("invalid_order_id")]
    [InlineData("invalid_product_code")]
    [InlineData("invalid_quantity")]
    [InlineData("invalid_customer_note")]
    public void Every_domain_failure_has_its_declared_transport_code(string code)
    {
        OrderError error = code switch
        {
            "empty_order" => new OrderError.EmptyOrder(),
            "too_many_order_lines" => new OrderError.TooManyLines(5, 6),
            "invalid_order_id" => new OrderError.InvalidOrderId(),
            "invalid_product_code" => new OrderError.InvalidProductCode(),
            "invalid_quantity" => new OrderError.InvalidQuantity(0),
            _ => new OrderError.InvalidCustomerNote()
        };
        var result = OrderEndpoint.ToResponse(new Result<OrderReceipt, OrderError>.Failure(error));
        var response = Assert.IsType<UnprocessableEntity<OrderErrorResponse>>(result.Result);
        Assert.Equal(422, response.StatusCode);
        Assert.Equal(code, Assert.IsType<OrderErrorResponse>(response.Value).Code);
    }

    [Fact]
    public void Transport_dtos_round_trip_without_serializing_domain_objects()
    {
        var request = new OrderRequest(Guid.NewGuid(), [new OrderLineRequest("SKU-1", 4)], "note");
        var restored = Assert.IsType<OrderRequest>(JsonSerializer.Deserialize<OrderRequest>(JsonSerializer.Serialize(request)));
        Assert.Equal(request.OrderId, restored.OrderId);
        Assert.Equal(request.CustomerNote, restored.CustomerNote);
        Assert.Equal(Assert.Single(request.Lines), Assert.Single(restored.Lines));
        var response = new OrderResponse(request.OrderId, 1);
        Assert.Equal(response, JsonSerializer.Deserialize<OrderResponse>(JsonSerializer.Serialize(response)));
    }

    [Fact]
    public async Task Application_forwards_the_exact_cancellation_token_and_stores_once()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingStore();
        var result = await new SubmitOrderHandler(store).HandleAsync(Submission(), cancellation.Token);
        Assert.IsType<Result<OrderReceipt, OrderError>.Success>(result);
        Assert.Equal(1, store.Calls);
        Assert.Equal(cancellation.Token, store.Token);
    }

    [Fact]
    public async Task Cancelled_storage_does_not_append_an_order()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var store = new InMemoryOrderStore();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SubmitOrderHandler(store).HandleAsync(Submission(), cancellation.Token));
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public async Task Storage_failure_propagates_without_a_success_receipt_or_retry()
    {
        var store = new RecordingStore(fail: true);
        await Assert.ThrowsAsync<IOException>(() => new SubmitOrderHandler(store).HandleAsync(Submission(), TestContext.Current.CancellationToken));
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task Storage_snapshot_is_independent_of_later_appends()
    {
        var store = new InMemoryOrderStore();
        var accepted = Accepted(Submission());
        await store.SaveAsync(accepted, TestContext.Current.CancellationToken);
        var before = store.Snapshot();
        await store.SaveAsync(accepted, TestContext.Current.CancellationToken);
        Assert.Single(before);
        Assert.Equal(2, store.Snapshot().Length);
    }

    private static OrderSubmission Submission(int quantity = 1, string code = "SKU-1") =>
        new(Guid.NewGuid(), [new OrderLineInput(code, quantity)], new Option<string>.None());

    private static AcceptedOrder Accepted(OrderSubmission submission) =>
        Assert.IsType<Result<AcceptedOrder, OrderError>.Success>(OrderDecisions.Accept(submission)).Value;

    private static OrderError Failure(Result<OrderReceipt, OrderError> result) =>
        Assert.IsType<Result<OrderReceipt, OrderError>.Failure>(result).Error;

    private sealed class RecordingStore(bool fail = false) : IOrderStore
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task SaveAsync(AcceptedOrder order, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(order);
            Calls++;
            Token = cancellationToken;
            return fail ? Task.FromException(new IOException("Declared sample storage failure.")) : Task.CompletedTask;
        }
    }
}
