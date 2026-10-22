using Inventory.Domain;
using Npgsql;

namespace Inventory.Adapter;

public sealed record StoreState(int Available, long Version, long Reservations, long Outbox);

public sealed class ReservationStore
{
    private readonly string _connection;
    private readonly Func<CancellationToken, ValueTask> _afterStockWrite;

    public ReservationStore(string connection) : this(connection, static _ => ValueTask.CompletedTask) { }
    public ReservationStore(string connection, Func<CancellationToken, ValueTask> afterStockWrite)
    {
        _connection = connection;
        _afterStockWrite = afterStockWrite;
    }

    public static Task<ReservationStore> CreateAsync(string connection, int stock, CancellationToken cancellationToken) =>
        InitializeAsync(connection, stock, "reservation_" + Guid.NewGuid().ToString("N"), cancellationToken);

    public static Task<ReservationStore> OpenPersistentAsync(string connection, int stock, CancellationToken cancellationToken) =>
        InitializeAsync(connection, stock, "inventory", cancellationToken);

    private static async Task<ReservationStore> InitializeAsync(string connection, int stock, string schema, CancellationToken cancellationToken)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Identifier consists exclusively of this fixed prefix and generated hex.
        await using var setup = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {schema}; SET search_path TO {schema}; CREATE TABLE IF NOT EXISTS stock (sku text PRIMARY KEY, available integer NOT NULL CHECK (available >= 0), version bigint NOT NULL); CREATE TABLE IF NOT EXISTS reservations (request_id text PRIMARY KEY, sku text NOT NULL, quantity integer NOT NULL, remaining integer NOT NULL, version bigint NOT NULL); CREATE TABLE IF NOT EXISTS outbox (id text PRIMARY KEY, kind text NOT NULL); INSERT INTO stock VALUES ('widget', @stock, 0) ON CONFLICT DO NOTHING;", database);
        setup.Parameters.AddWithValue("stock", stock);
        await setup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var scoped = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema };
        return new ReservationStore(scoped.ConnectionString);
    }

    public ReservationStore WithFault(Func<CancellationToken, ValueTask> fault) => new(_connection, fault);
    public ReservationStore Reopen() => new(_connection);

    public async Task<ReservationDecision> ReserveAsync(ReservationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = ReservationDecisions.Validate(request);
        if (validation is InvalidReservation invalid) { return invalid; }
        await using var connection = new NpgsqlConnection(_connection);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction))
        {
            guard.Parameters.AddWithValue("key", request.RequestId);
            await guard.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        PriorReservation prior = new NoReservation();
        await using (var lookup = new NpgsqlCommand("SELECT sku, quantity, remaining, version FROM reservations WHERE request_id=@key", connection, transaction))
        {
            lookup.Parameters.AddWithValue("key", request.RequestId);
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                prior = new ExistingReservation(request.RequestId, reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt64(3));
            }
        }
        var stock = new Stock(0, 0);
        await using (var lookup = new NpgsqlCommand("SELECT available, version FROM stock WHERE sku=@sku FOR UPDATE", connection, transaction))
        {
            lookup.Parameters.AddWithValue("sku", request.Sku);
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { stock = new(reader.GetInt32(0), reader.GetInt64(1)); }
        }
        var decision = ReservationDecisions.Decide(request, stock, prior);
        if (decision is not CommitReservation commit) { return decision; }
        await using (var update = new NpgsqlCommand("UPDATE stock SET available=@remaining, version=@version WHERE sku=@sku", connection, transaction))
        {
            update.Parameters.AddWithValue("remaining", commit.Remaining); update.Parameters.AddWithValue("version", commit.Version); update.Parameters.AddWithValue("sku", commit.Sku);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) { throw new InvalidDataException("Stock update did not affect exactly one row."); }
        }
        await _afterStockWrite(cancellationToken).ConfigureAwait(false);
        await using (var insert = new NpgsqlCommand("INSERT INTO reservations VALUES (@key, @sku, @quantity, @remaining, @version); INSERT INTO outbox VALUES (@key, 'ReservationCreated');", connection, transaction))
        {
            insert.Parameters.AddWithValue("key", commit.RequestId); insert.Parameters.AddWithValue("sku", commit.Sku); insert.Parameters.AddWithValue("quantity", commit.Quantity);
            insert.Parameters.AddWithValue("remaining", commit.Remaining); insert.Parameters.AddWithValue("version", commit.Version);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return commit;
    }

    public async Task<StoreState> InspectAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connection);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var query = new NpgsqlCommand("SELECT available, version, (SELECT count(*) FROM reservations), (SELECT count(*) FROM outbox) FROM stock WHERE sku='widget'", connection);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { throw new InvalidDataException("Stock was not initialized."); }
        return new(reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }
}
