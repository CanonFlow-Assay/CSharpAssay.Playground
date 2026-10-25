using System.Collections.Immutable;
using ExpenseGuard.Application;
using ExpenseGuard.Domain;
using Npgsql;

namespace ExpenseGuard.Infrastructure;

public sealed class PostgresExpenseStore(NpgsqlDataSource data, TimeProvider time, ReceiptBlobStore? receiptStore = null) : IExpenseStore
{
    // A bounded module deliberately serializes ledger writes. This also prevents
    // two simultaneous claims both clearing duplicate/daily-total checks.
    private const long LedgerLock = 714023119;
    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var command = data.CreateCommand("""
            CREATE TABLE IF NOT EXISTS expense_claims (
              id uuid PRIMARY KEY, version integer NOT NULL CHECK(version > 0), document text NOT NULL);
            CREATE TABLE IF NOT EXISTS expense_receipts (
              claim_id uuid NOT NULL REFERENCES expense_claims(id), sequence integer NOT NULL,
              event text NOT NULL, actor text NOT NULL, role text NOT NULL, at timestamptz NOT NULL,
              snapshot text NOT NULL, previous_hash text NOT NULL, hash text NOT NULL,
              PRIMARY KEY(claim_id, sequence));
            CREATE TABLE IF NOT EXISTS expense_outbox (
              claim_id uuid PRIMARY KEY REFERENCES expense_claims(id),
              status text NOT NULL CHECK(status IN ('Pending','Completed','Cancelled','ManualReview')),
              idempotency_key text NOT NULL UNIQUE);
            ALTER TABLE expense_outbox ADD COLUMN IF NOT EXISTS attempts integer NOT NULL DEFAULT 0;
            ALTER TABLE expense_outbox ADD COLUMN IF NOT EXISTS available_at timestamptz NOT NULL DEFAULT '-infinity';
            ALTER TABLE expense_outbox ADD COLUMN IF NOT EXISTS last_error_code text NOT NULL DEFAULT '';
            ALTER TABLE expense_outbox DROP CONSTRAINT IF EXISTS expense_outbox_status_check;
            ALTER TABLE expense_outbox ADD CONSTRAINT expense_outbox_status_check CHECK(status IN ('Pending','Completed','Cancelled','ManualReview'));
            """);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<StoreResult> ExecuteAsync(ExpenseId id, Actor actor, Func<Ledger, Mutation> decide, CancellationToken ct)
    {
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, transaction, ct);
        var claims = ImmutableArray.CreateBuilder<ExpenseClaim>();
        var persisted = new List<(Guid Id, int Version, string Snapshot)>();
        ExpenseClaim? current = null;
        var version = 0;
        await using (var read = new NpgsqlCommand("SELECT id, version, document FROM expense_claims ORDER BY id", connection, transaction))
        await using (var reader = await read.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var claim = ClaimDocument.Parse(reader.GetString(2)).ToDomain();
                if (claim.Id.Value != reader.GetGuid(0)) throw new InvalidDataException("Stored claim identity differs from its ledger row.");
                persisted.Add((reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2)));
                claims.Add(claim);
                if (reader.GetGuid(0) == id.Value) { current = claim; version = reader.GetInt32(1); }
            }
        }
        foreach (var row in persisted)
            RequireIntegrity(await ReceiptsAsync(connection, transaction, row.Id, ct), row.Version, row.Snapshot);
        var mutation = decide(new(current, version, claims.ToImmutable()));
        if (mutation.Error.Length > 0) return new(current, version, mutation.Error);
        if (mutation.QueuePayment)
        {
            await using var queue = new NpgsqlCommand("INSERT INTO expense_outbox(claim_id,status,idempotency_key) VALUES ($1,'Pending',$2) ON CONFLICT DO NOTHING", connection, transaction);
            queue.Parameters.AddWithValue(id.Value);
            queue.Parameters.AddWithValue("expense/" + id.Value.ToString("N"));
            // Initial claims exist before payment requests; FK protects this path.
            if (await queue.ExecuteNonQueryAsync(ct) != 1) return new(current, version, "PaymentAlreadyRequested");
        }
        foreach (var change in mutation.Changes)
        {
            if (change.Claim.Id != id) throw new InvalidOperationException("Mutation changed claim identity.");
            if (change.Claim.State is Submitted or PolicyChecked or ManagerApprovalRequired or FinanceApprovalRequired or Approved or Reimbursed)
                await RequireReceiptAsync(change.Claim, ct);
            await AppendAsync(connection, transaction, change, actor, ++version, ct);
            current = change.Claim;
        }
        await transaction.CommitAsync(ct);
        return new(current, version, "");
    }

    public async Task<EvidenceBundle?> ReadAsync(ExpenseId id, CancellationToken ct)
    {
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        string snapshot;
        int version;
        await using (var command = new NpgsqlCommand("SELECT version, document FROM expense_claims WHERE id=$1", connection, transaction))
        {
            command.Parameters.AddWithValue(id.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            version = reader.GetInt32(0); snapshot = reader.GetString(1);
        }
        var items = await ReceiptsAsync(connection, transaction, id.Value, ct);
        await transaction.CommitAsync(ct);
        var valid = ReceiptHashes.Verify(items) && items.Length == version && items[^1].SnapshotJson == snapshot;
        return new(ClaimDocument.Parse(snapshot).ToDomain(), version, items, valid);
    }

    public async Task<ImmutableArray<ListedClaim>> ListAsync(Actor actor, CancellationToken ct)
    {
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var rows = new List<(ExpenseClaim Claim, int Version, string Snapshot)>();
        await using (var command = new NpgsqlCommand("SELECT version,document FROM expense_claims ORDER BY id", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                var snapshot = reader.GetString(1); var claim = ClaimDocument.Parse(snapshot).ToDomain();
                if (Access.CanRead(claim, actor)) rows.Add((claim, reader.GetInt32(0), snapshot));
            }
        var result = ImmutableArray.CreateBuilder<ListedClaim>();
        foreach (var row in rows)
        {
            var receipts = await ReceiptsAsync(connection, transaction, row.Claim.Id.Value, ct);
            result.Add(new(row.Claim, row.Version, ReceiptHashes.Verify(receipts) && receipts.Length == row.Version && receipts[^1].SnapshotJson == row.Snapshot));
        }
        await transaction.CommitAsync(ct);
        return result.ToImmutable();
    }

    public async Task<bool> ProcessOneAsync(IPaymentGateway gateway, CancellationToken ct)
    {
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, transaction, ct);
        Guid id;
        string key;
        int attempts;
        await using (var find = new NpgsqlCommand("SELECT claim_id,idempotency_key,attempts FROM expense_outbox WHERE status='Pending' AND available_at <= $1 ORDER BY available_at,claim_id LIMIT 1 FOR UPDATE SKIP LOCKED", connection, transaction))
        {
        find.Parameters.AddWithValue(time.GetUtcNow());
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            id = reader.GetGuid(0); key = reader.GetString(1); attempts = reader.GetInt32(2);
        }
        }
        // Retain the selected row lock while rolling back any failed payment/audit
        // work. Retry metadata commits independently of those rolled-back changes.
        await transaction.SaveAsync("payment_attempt", ct);
        try
        {
        ExpenseClaim claim;
        int version;
        await using (var read = new NpgsqlCommand("SELECT version,document FROM expense_claims WHERE id=$1 FOR UPDATE", connection, transaction))
        {
            read.Parameters.AddWithValue(id);
            await using var reader = await read.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new InvalidDataException("Outbox claim missing.");
            version = reader.GetInt32(0); claim = ClaimDocument.Parse(reader.GetString(1)).ToDomain();
        }
        RequireIntegrity(await ReceiptsAsync(connection, transaction, id, ct), version, ClaimDocument.From(claim).Serialize());
        var status = "Cancelled";
        if (claim.State is Approved && claim.Policy is Assessment)
        {
            await RequireReceiptAsync(claim, ct);
            var reference = await gateway.PayAsync(claim, key, ct);
            var actor = new Actor(new("payment-worker"), Role.PaymentWorker);
            var transition = Transitions.Decide(claim, new Reimburse(reference), actor);
            if (transition is not Accepted result) throw new InvalidDataException("Payment transition rejected.");
            await AppendAsync(connection, transaction, new(result.Claim, result.Event), actor, version + 1, ct);
            status = "Completed";
        }
        await using var finish = new NpgsqlCommand("UPDATE expense_outbox SET status=$2 WHERE claim_id=$1", connection, transaction);
        finish.Parameters.AddWithValue(id); finish.Parameters.AddWithValue(status);
        await finish.ExecuteNonQueryAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync("payment_attempt", ct);
            var manual = exception is InvalidDataException || attempts + 1 >= 3;
            await using var retry = new NpgsqlCommand("UPDATE expense_outbox SET status=$2,attempts=attempts+1,available_at=$3,last_error_code=$4 WHERE claim_id=$1", connection, transaction);
            retry.Parameters.AddWithValue(id); retry.Parameters.AddWithValue(manual ? "ManualReview" : "Pending");
            retry.Parameters.AddWithValue(time.GetUtcNow().AddSeconds(attempts == 0 ? 30 : 120));
            retry.Parameters.AddWithValue(exception is InvalidDataException ? "IntegrityFailure" : "ProcessingFailure");
            await retry.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
            throw;
        }
        // A commit exception is an uncertain outcome: never send a new key or
        // assert rollback. A retry uses the same key if the row remains Pending.
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<PaymentWork?> ReadPaymentWorkAsync(ExpenseId id, CancellationToken ct)
    {
        await using var command = data.CreateCommand("SELECT status,attempts,available_at,last_error_code,idempotency_key FROM expense_outbox WHERE claim_id=$1");
        command.Parameters.AddWithValue(id.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetString(0), reader.GetInt32(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3), reader.GetString(4)) : null;
    }

    private async Task RequireReceiptAsync(ExpenseClaim claim, CancellationToken ct)
    {
        if (claim.Receipt is Receipt external && !external.Reference.StartsWith("blob:", StringComparison.Ordinal) &&
            claim.State is ManagerApprovalRequired or FinanceApprovalRequired or Approved or Reimbursed)
            throw new InvalidDataException("External assertions cannot authorize approval or payment; verified stored bytes and reassessment required.");
        if (claim.Receipt is Receipt r && r.Reference.StartsWith("blob:", StringComparison.Ordinal) &&
            (receiptStore is null || await receiptStore.ReadAsync(claim.Id.Value, r.Reference, r.Sha256, ct) is null))
            throw new InvalidDataException("Stored receipt is unavailable; approval processing and payment are blocked.");
    }

    private static void RequireIntegrity(ImmutableArray<AuditReceipt> receipts, int version, string snapshot)
    {
        if (!ReceiptHashes.Verify(receipts) || receipts.Length != version || receipts[^1].SnapshotJson != snapshot)
            throw new InvalidDataException("Ledger integrity validation failed; no state change or payment is authorized.");
    }

    private static async Task<ImmutableArray<AuditReceipt>> ReceiptsAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid id, CancellationToken ct)
    {
        var receipts = ImmutableArray.CreateBuilder<AuditReceipt>();
        await using var command = new NpgsqlCommand("SELECT sequence,event,actor,role,at,snapshot,previous_hash,hash FROM expense_receipts WHERE claim_id=$1 ORDER BY sequence", c, t);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) receipts.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            new DateTimeOffset(reader.GetDateTime(4)), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        return receipts.ToImmutable();
    }

    private static async Task LockAsync(NpgsqlConnection c, NpgsqlTransaction t, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", c, t);
        command.Parameters.AddWithValue(LedgerLock);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task AppendAsync(NpgsqlConnection c, NpgsqlTransaction t, Change change, Actor actor, int version, CancellationToken ct)
    {
        var snapshot = ClaimDocument.From(change.Claim).Serialize();
        var previous = "GENESIS";
        await using (var find = new NpgsqlCommand("SELECT hash FROM expense_receipts WHERE claim_id=$1 ORDER BY sequence DESC LIMIT 1", c, t))
        {
            find.Parameters.AddWithValue(change.Claim.Id.Value);
            if (await find.ExecuteScalarAsync(ct) is string hash) previous = hash;
        }
        var now = time.GetUtcNow();
        var at = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        var digest = ReceiptHashes.Compute(previous, version, change.Event, actor.Id.Value, actor.Role.ToString(), at, snapshot);
        await using var save = new NpgsqlCommand("""
            INSERT INTO expense_claims(id,version,document) VALUES ($1,$2,$3)
              ON CONFLICT(id) DO UPDATE SET version=EXCLUDED.version, document=EXCLUDED.document
            """, c, t);
        save.Parameters.AddWithValue(change.Claim.Id.Value); save.Parameters.AddWithValue(version);
        save.Parameters.AddWithValue(snapshot);
        await save.ExecuteNonQueryAsync(ct);
        await using var audit = new NpgsqlCommand("""
            INSERT INTO expense_receipts(claim_id,sequence,event,actor,role,at,snapshot,previous_hash,hash)
              VALUES ($1,$2,$4,$5,$6,$7,$3,$8,$9)
            """, c, t);
        audit.Parameters.AddWithValue(change.Claim.Id.Value); audit.Parameters.AddWithValue(version);
        audit.Parameters.AddWithValue(snapshot); audit.Parameters.AddWithValue(change.Event);
        audit.Parameters.AddWithValue(actor.Id.Value); audit.Parameters.AddWithValue(actor.Role.ToString());
        audit.Parameters.AddWithValue(at); audit.Parameters.AddWithValue(previous); audit.Parameters.AddWithValue(digest);
        await audit.ExecuteNonQueryAsync(ct);
    }
}

public sealed record PaymentWork(string Status, int Attempts, DateTimeOffset AvailableAt, string LastErrorCode, string IdempotencyKey);
