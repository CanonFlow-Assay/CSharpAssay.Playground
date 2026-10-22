using System.Net;
using System.Net.Http.Json;
using Inventory.Adapter;
using Inventory.Api;
using Inventory.Domain;
using Xunit;
namespace Inventory.Tests;

public sealed class LimitTests
{
    [Fact]
    public void OverLimitDomainRequestIsInvalid() => Assert.IsType<InvalidReservation>(ReservationDecisions.Decide(new("limit", "widget", 4), new(10, 0), new NoReservation()).Value);
    [Fact]
    public async Task HttpOverLimitRequestDoesNotWrite()
    {
        var token = TestContext.Current.CancellationToken;
        var connection = Environment.GetEnvironmentVariable("ASSAY_INVENTORY_DATABASE") ?? throw new InvalidOperationException("Real database required.");
        var store = await ReservationStore.CreateAsync(connection, 10, token);
        await using var server = await Server.Start(store, token);
        using var response = await server.Client.PostAsJsonAsync("/reservations", new ReservationRequestDto("limit", "widget", 4), token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new StoreState(10, 0, 0, 0), await store.InspectAsync(token));
    }
}
