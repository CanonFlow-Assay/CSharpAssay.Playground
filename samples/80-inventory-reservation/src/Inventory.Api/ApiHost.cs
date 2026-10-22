using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assay.Workflow.Domain;
using Inventory.Adapter;
using Inventory.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Inventory.Api;

public sealed record ReservationResponse(string Status, string ReservationId, int Remaining, string Reason);
public sealed record ReservationRequestDto(string? RequestId, string? Sku, int Quantity);
public sealed record HttpProjection(int StatusCode, ReservationResponse Response);

public static class ApiHost
{
    public static WebApplication Create(ReservationStore store, string url)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Urls.Add(url);
        app.MapPost("/reservations", async (ReservationRequestDto request, CancellationToken token) =>
        {
            var outcome = request.RequestId is string key && request.Sku is string sku
                ? await store.ReserveAsync(new(key, sku, request.Quantity), token).ConfigureAwait(false)
                : new InvalidReservation("Request key and SKU are required.");
            var projection = Project(outcome);
            return Results.Json(projection.Response, statusCode: projection.StatusCode);
        });
        return app;
    }

    public static HttpProjection Project(ReservationDecision decision) => decision switch
    {
        CommitReservation c => new(201, new("reserved", c.RequestId, c.Remaining, "")),
        ReplayReservation r => new(200, new("replayed", r.Receipt.RequestId, r.Receipt.Remaining, "")),
        InvalidReservation i => new(400, new("invalid", "", 0, i.Reason)),
        InsufficientStock s => new(409, new("out-of-stock", "", s.Available, "")),
        ReservationConflict c => new(409, new("conflict", "", 0, c.Reason))
    };

    public static ContractSnapshot ObserveContract()
    {
        var members = typeof(ReservationResponse).GetProperties().Select(p => new ContractMember(
            p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name),
            p.PropertyType.FullName ?? p.PropertyType.Name, true)).ToImmutableArray();
        ReservationDecision[] cases = [new CommitReservation("key", "widget", 1, 4, 1),
            new ReplayReservation(new("key", "widget", 1, 4, 1)), new InvalidReservation("reason"),
            new InsufficientStock(0), new ReservationConflict("reason")];
        return new(members, cases.Select(c => Project(c).Response.Status).ToImmutableArray());
    }
}
