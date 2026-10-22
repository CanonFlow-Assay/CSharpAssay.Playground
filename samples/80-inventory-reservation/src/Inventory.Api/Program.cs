using Inventory.Adapter;
using Inventory.Api;

var connection = Environment.GetEnvironmentVariable("ASSAY_INVENTORY_DATABASE")
    ?? throw new InvalidOperationException("Configure the inventory database before starting the API.");
var store = await ReservationStore.OpenPersistentAsync(connection, 100, CancellationToken.None);
await using var app = ApiHost.Create(store, "http://127.0.0.1:5080");
await app.RunAsync();
