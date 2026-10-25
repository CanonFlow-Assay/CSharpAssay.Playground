using ExpenseGuard.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);
var connection = builder.Configuration["ConnectionStrings:Expenses"] ?? throw new InvalidOperationException("Expenses connection string required.");
await using var data = NpgsqlDataSource.Create(connection);
var receiptPath = builder.Configuration["ExpenseGuard:ReceiptDirectory"];
var store = new PostgresExpenseStore(data, TimeProvider.System, receiptPath is null ? null : new ReceiptBlobStore(Path.GetFullPath(receiptPath)));
var gateway = new SimulatedPaymentGateway();
using var host = builder.Build();
await host.StartAsync();
var lifetime = host.Services.GetService(typeof(IHostApplicationLifetime)) as IHostApplicationLifetime
    ?? throw new InvalidOperationException("Missing host lifetime.");
var logger = host.Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory
    ?? throw new InvalidOperationException("Missing logger.");
var log = logger.CreateLogger("ExpenseGuard.Worker");
try
{
    while (!lifetime.ApplicationStopping.IsCancellationRequested)
    {
        try { await store.ProcessOneAsync(gateway, lifetime.ApplicationStopping); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { log.LogError(exception, "Payment processing failed; transaction rolled back and pending work will retry with the same key."); }
        await Task.Delay(TimeSpan.FromSeconds(2), lifetime.ApplicationStopping);
    }
}
catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
{ log.LogInformation("Worker stopped."); }
await host.StopAsync();
