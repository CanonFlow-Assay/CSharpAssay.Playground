using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assay.Workflow.Domain;
using Inventory.Adapter;
using Inventory.Api;
using Inventory.Domain;

var connection = Environment.GetEnvironmentVariable("ASSAY_INVENTORY_DATABASE")
    ?? throw new InvalidOperationException("A real PostgreSQL database is required.");
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
var token = deadline.Token;
var observations = ImmutableArray.CreateBuilder<WorkerScenario>();
foreach (var id in new[] { "reserve", "replay", "conflict", "out-of-stock", "invalid", "rollback", "cancel-before" })
{
    var clock = Stopwatch.StartNew();
    var trace = ImmutableArray.CreateBuilder<OperationTrace>();
    trace.Add(new(id, 1, 1, clock.Elapsed.Ticks, TraceKind.Attempt));
    var store = await ReservationStore.CreateAsync(connection, id == "out-of-stock" ? 1 : 5, token);
    var passed = false;
    var terminal = TraceKind.Success;
    if (id == "rollback")
    {
        terminal = TraceKind.PermanentFailure;
        var faulted = store.WithFault(_ => ValueTask.FromException(new InvalidOperationException("Injected rollback probe.")));
        try { await faulted.ReserveAsync(new(id, "widget", 2), token); }
        catch (InvalidOperationException) { passed = await store.InspectAsync(token) == new StoreState(5, 0, 0, 0); }
    }
    else if (id == "cancel-before")
    {
        terminal = TraceKind.Cancelled;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await store.ReserveAsync(new(id, "widget", 2), cancelled.Token); }
        catch (OperationCanceledException) { passed = await store.InspectAsync(token) == new StoreState(5, 0, 0, 0); }
    }
    else
    {
        await using var api = ApiHost.Create(store, "http://127.0.0.1:0");
        await api.StartAsync(token);
        using var http = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
        var quantity = id == "invalid" ? 0 : 2;
        using var first = await http.PostAsJsonAsync("/reservations", new ReservationRequestDto(id, "widget", quantity), token);
        terminal = first.StatusCode switch
        {
            HttpStatusCode.BadRequest => TraceKind.PermanentFailure,
            HttpStatusCode.Conflict => TraceKind.Rejected,
            _ => TraceKind.Success
        };
        var response = await first.Content.ReadFromJsonAsync<ReservationResponse>(token) ?? throw new InvalidDataException("Missing HTTP response.");
        if (id is "replay" or "conflict")
        {
            trace.Add(new(id, trace.Count + 1, 2, clock.Elapsed.Ticks, TraceKind.Attempt));
            using var again = await http.PostAsJsonAsync("/reservations", new ReservationRequestDto(id, "widget", id == "conflict" ? 3 : quantity), token);
            terminal = id == "conflict" ? TraceKind.Rejected : TraceKind.Success;
            passed = first.StatusCode == HttpStatusCode.Created && again.StatusCode == (id == "conflict" ? HttpStatusCode.Conflict : HttpStatusCode.OK)
                && await store.InspectAsync(token) == new StoreState(3, 1, 1, 1);
        }
        else
        {
            passed = id switch
            {
                "reserve" => first.StatusCode == HttpStatusCode.Created && response.Remaining == 3 && await store.InspectAsync(token) == new StoreState(3, 1, 1, 1),
                "out-of-stock" => first.StatusCode == HttpStatusCode.Conflict && await store.InspectAsync(token) == new StoreState(1, 0, 0, 0),
                _ => first.StatusCode == HttpStatusCode.BadRequest && await store.InspectAsync(token) == new StoreState(5, 0, 0, 0)
            };
        }
    }
    var state = await store.InspectAsync(token);
    if (state.Outbox == 1) { trace.Add(new(id, trace.Count + 1, 1, clock.Elapsed.Ticks, TraceKind.SideEffect)); }
    trace.Add(new(id, trace.Count + 1, 1, clock.Elapsed.Ticks, terminal));
    observations.Add(new(id, "reservation", passed, trace.ToImmutable()));
}
var checks = ImmutableArray.CreateBuilder<WorkerCheck>();
var stockStore = await ReservationStore.CreateAsync(connection, 5, token);
var races = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => stockStore.ReserveAsync(new("race-" + i, "widget", 1), token)));
checks.Add(new("concurrent-stock-conservation", races.Count(r => r is CommitReservation) == 5 && await stockStore.InspectAsync(token) == new StoreState(0, 5, 5, 5)));
var keyStore = await ReservationStore.CreateAsync(connection, 5, token);
var replays = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => keyStore.ReserveAsync(new("same", "widget", 2), token)));
checks.Add(new("concurrent-key-idempotency", replays.Count(r => r is CommitReservation) == 1 && await keyStore.InspectAsync(token) == new StoreState(3, 1, 1, 1)));
checks.Add(new("connection-restart-persistence", await keyStore.Reopen().ReserveAsync(new("same", "widget", 2), token) is ReplayReservation && await keyStore.InspectAsync(token) == new StoreState(3, 1, 1, 1)));
var execution = new WorkerExecution(WorkerExecution.ProtocolSchema, "completed", observations.ToImmutable(), checks.ToImmutable(), ApiHost.ObserveContract());
Console.WriteLine(JsonSerializer.Serialize(execution, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
}));
