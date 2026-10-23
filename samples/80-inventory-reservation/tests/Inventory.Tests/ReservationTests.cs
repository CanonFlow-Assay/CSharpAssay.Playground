using System.Net;
using System.Net.Http.Json;
using Inventory.Adapter;
using Inventory.Api;
using Inventory.Domain;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Inventory.Tests;

public sealed class ReservationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Connection => Environment.GetEnvironmentVariable("ASSAY_INVENTORY_DATABASE")
        ?? throw new InvalidOperationException("Run the inventory database wrapper; tests cannot skip database evidence.");
    private static Task<ReservationStore> Store(int stock = 5) => ReservationStore.CreateAsync(Connection, stock, Token);

    [Fact]
    public void ZeroQuantityIsInvalid() => Assert.IsType<InvalidReservation>(ReservationDecisions.Decide(new("key", "widget", 0), new(5, 0), new NoReservation()).Value);
    [Fact]
    public void NegativeQuantityIsInvalid() => Assert.IsType<InvalidReservation>(ReservationDecisions.Decide(new("key", "widget", -1), new(5, 0), new NoReservation()).Value);
    [Fact]
    public void MissingRequestKeyIsInvalid() => Assert.IsType<InvalidReservation>(ReservationDecisions.Decide(new("", "widget", 1), new(5, 0), new NoReservation()).Value);
    [Fact]
    public void EnoughStockProducesExplicitCommit() => Assert.Equal(new CommitReservation("key", "widget", 2, 3, 8), Assert.IsType<CommitReservation>(ReservationDecisions.Decide(new("key", "widget", 2), new(5, 7), new NoReservation()).Value));
    [Fact]
    public void InsufficientStockProducesExplicitRejection() => Assert.Equal(new InsufficientStock(2), Assert.IsType<InsufficientStock>(ReservationDecisions.Decide(new("key", "widget", 3), new(2, 0), new NoReservation()).Value));
    [Fact]
    public void PriorRequestProducesReplay() => Assert.Equal(new ReplayReservation(new("key", "widget", 2, 3, 1)), Assert.IsType<ReplayReservation>(ReservationDecisions.Decide(new("key", "widget", 2), new(0, 1), new ExistingReservation("key", "widget", 2, 3, 1)).Value));
    [Fact]
    public void ChangedPriorQuantityProducesConflict() => Assert.IsType<ReservationConflict>(ReservationDecisions.Decide(new("key", "widget", 3), new(5, 1), new ExistingReservation("key", "widget", 2, 3, 1)).Value);
    [Fact]
    public void ChangedPriorSkuProducesConflict() => Assert.IsType<ReservationConflict>(ReservationDecisions.Decide(new("key", "other", 2), new(5, 1), new ExistingReservation("key", "widget", 2, 3, 1)).Value);
    [Fact]
    public void HistoricalReceiptRemainsReplayable() => Assert.IsType<ReplayReservation>(ReservationDecisions.Decide(new("legacy", "widget", 4), new(1, 1), new ExistingReservation("legacy", "widget", 4, 1, 1)).Value);
    [Fact]
    public void VersionOverflowCannotWrap() => Assert.Throws<OverflowException>(() => ReservationDecisions.Decide(new("key", "widget", 1), new(5, long.MaxValue), new NoReservation()));

    [Fact]
    public async Task CommitWritesStockReservationAndOutbox()
    {
        var store = await Store();
        Assert.IsType<CommitReservation>((await store.ReserveAsync(new("key", "widget", 2), Token)).Value);
        Assert.Equal(new StoreState(3, 1, 1, 1), await store.InspectAsync(Token));
    }
    [Fact]
    public async Task ReplayDoesNotDuplicateEffects()
    {
        var store = await Store();
        await store.ReserveAsync(new("key", "widget", 2), Token);
        Assert.IsType<ReplayReservation>((await store.ReserveAsync(new("key", "widget", 2), Token)).Value);
        Assert.Equal(new StoreState(3, 1, 1, 1), await store.InspectAsync(Token));
    }
    [Fact]
    public async Task SameKeyChangedInputDoesNotMutateStock()
    {
        var store = await Store();
        await store.ReserveAsync(new("key", "widget", 2), Token);
        Assert.IsType<ReservationConflict>((await store.ReserveAsync(new("key", "widget", 3), Token)).Value);
        Assert.Equal(new StoreState(3, 1, 1, 1), await store.InspectAsync(Token));
    }
    [Fact]
    public async Task ConcurrentReservationsConserveStock()
    {
        var store = await Store();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => store.ReserveAsync(new("key-" + i, "widget", 1), Token)));
        Assert.Equal(5, results.Count(r => r is CommitReservation));
        Assert.Equal(7, results.Count(r => r is InsufficientStock));
        Assert.Equal(new StoreState(0, 5, 5, 5), await store.InspectAsync(Token));
    }
    [Fact]
    public async Task ConcurrentReplaysCommitOnce()
    {
        var store = await Store();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.ReserveAsync(new("same", "widget", 2), Token)));
        Assert.Single(results, r => r is CommitReservation);
        Assert.Equal(7, results.Count(r => r is ReplayReservation));
        Assert.Equal(new StoreState(3, 1, 1, 1), await store.InspectAsync(Token));
    }
    [Fact]
    public async Task ExceptionAfterStockWriteRollsBackAllTables()
    {
        var store = await Store();
        var faulted = store.WithFault(_ => ValueTask.FromException(new InvalidOperationException("Injected failure after stock update.")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => faulted.ReserveAsync(new("fault", "widget", 2), Token));
        Assert.Equal(new StoreState(5, 0, 0, 0), await store.InspectAsync(Token));
    }
    [Fact]
    public async Task CancellationAfterStockWriteRollsBack()
    {
        var store = await Store();
        using var cancellation = new CancellationTokenSource();
        var faulted = store.WithFault(_ => { cancellation.Cancel(); return ValueTask.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => faulted.ReserveAsync(new("cancelled", "widget", 2), cancellation.Token));
        Assert.Equal(new StoreState(5, 0, 0, 0), await store.InspectAsync(Token));
    }
    [Fact]
    public async Task ReopenedStoreRetainsReceipt()
    {
        var store = await Store();
        await store.ReserveAsync(new("persist", "widget", 1), Token);
        Assert.IsType<ReplayReservation>((await store.Reopen().ReserveAsync(new("persist", "widget", 1), Token)).Value);
        Assert.Equal(new StoreState(4, 1, 1, 1), await store.InspectAsync(Token));
    }
    [Fact]
    public async Task UnknownSkuHasNoEffects()
    {
        var store = await Store();
        Assert.IsType<InsufficientStock>((await store.ReserveAsync(new("missing", "unknown", 1), Token)).Value);
        Assert.Equal(new StoreState(5, 0, 0, 0), await store.InspectAsync(Token));
    }
    [Fact]
    public async Task HttpReserveReturnsCreatedReceipt()
    {
        await using var server = await Server.Start(await Store(), Token);
        using var response = await server.Client.PostAsJsonAsync("/reservations", new ReservationRequestDto("http", "widget", 2), Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(Token);
        Assert.Equal(new("reserved", "http", 3, ""), body);
    }
    [Fact]
    public async Task HttpReplayReturnsStableReceipt()
    {
        await using var server = await Server.Start(await Store(), Token);
        using var original = await server.Client.PostAsJsonAsync("/reservations", new ReservationRequestDto("http", "widget", 2), Token);
        using var replay = await server.Client.PostAsJsonAsync("/reservations", new ReservationRequestDto("http", "widget", 2), Token);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var body = await replay.Content.ReadFromJsonAsync<ReservationResponse>(Token);
        Assert.Equal(new("replayed", "http", 3, ""), body);
    }
    [Fact]
    public async Task HttpInsufficientStockReturnsConflict()
    {
        await using var server = await Server.Start(await Store(1), Token);
        using var response = await server.Client.PostAsJsonAsync("/reservations", new ReservationRequestDto("http", "widget", 2), Token);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
    [Fact]
    public async Task HttpMissingKeyReturnsBadRequest()
    {
        await using var server = await Server.Start(await Store(), Token);
        using var response = await server.Client.PostAsJsonAsync("/reservations", new ReservationRequestDto(null, "widget", 1), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task HttpKeyConflictReturnsConflict()
    {
        await using var server = await Server.Start(await Store(), Token);
        using var first = await server.Client.PostAsJsonAsync("/reservations", new ReservationRequestDto("http", "widget", 2), Token);
        using var conflict = await server.Client.PostAsJsonAsync("/reservations", new ReservationRequestDto("http", "widget", 3), Token);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }
    [Fact]
    public async Task UnknownHttpRouteReturnsNotFound()
    {
        await using var server = await Server.Start(await Store(), Token);
        using var response = await server.Client.GetAsync("/unknown", Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public sealed class Server(WebApplication app, HttpClient client) : IAsyncDisposable
{
    public HttpClient Client { get; } = client;
    public static async Task<Server> Start(ReservationStore store, CancellationToken token)
    {
        var app = ApiHost.Create(store, "http://127.0.0.1:0");
        await app.StartAsync(token);
        return new(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) });
    }
    public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
}
