using System.Text.Json;
using Microsoft.Data.Sqlite;
using Playground.Saga.Domain;

namespace Playground.Saga.Demo;

public sealed record DispatchIntent(string Key, string Kind, string Payload);
public sealed record DeliveryCount(int Attempts, int Effects);

public sealed class SagaOutbox
{
    private readonly string path;

    public SagaOutbox(string path)
    {
        this.path = path;
        using var db = Sqlite.Open(path);
        Sqlite.Execute(db, """
            CREATE TABLE IF NOT EXISTS journal (
                transfer TEXT NOT NULL, sequence INTEGER NOT NULL, digest TEXT NOT NULL,
                observed_ticks INTEGER NOT NULL, entry TEXT NOT NULL, PRIMARY KEY(transfer, sequence)
            );
            CREATE TABLE IF NOT EXISTS outbox (
                key TEXT PRIMARY KEY, transfer TEXT NOT NULL, sequence INTEGER NOT NULL,
                kind TEXT NOT NULL, payload TEXT NOT NULL, status TEXT NOT NULL,
                FOREIGN KEY(transfer, sequence) REFERENCES journal(transfer, sequence)
            );
            """);
    }

    public void Record(string transfer, JournalEntry entry)
    {
        if (!EvidenceJournal.HasValidHash(entry))
        {
            throw new InvalidDataException("Invalid journal entry digest.");
        }

        using var db = Sqlite.Open(path);
        using var transaction = db.BeginTransaction(deferred: false);
        using var recorded = Sqlite.Command(db, "SELECT digest FROM journal WHERE transfer=$transfer AND sequence=$sequence",
            ("$transfer", transfer), ("$sequence", entry.Sequence));
        recorded.Transaction = transaction;
        if (recorded.ExecuteScalar() is string existing)
        {
            if (existing != entry.Sha256) { throw new InvalidDataException("Sequence is already bound to a different decision."); }
            return;
        }

        CheckPredecessor(db, transaction, transfer, entry);
        using var insert = Sqlite.Command(db, """
            INSERT INTO journal(transfer, sequence, digest, observed_ticks, entry) VALUES($transfer,$sequence,$digest,$time,$entry)
            """, ("$transfer", transfer), ("$sequence", entry.Sequence), ("$digest", entry.Sha256),
            ("$time", entry.Observation.Time.UtcTicks), ("$entry", JsonSerializer.Serialize(entry)));
        insert.Transaction = transaction;
        insert.ExecuteNonQuery();

        var observation = entry.Observation;
        var frozen = observation.State is nameof(Disputed) or nameof(PartnerReview);
        var moneyIntent = observation.Action is nameof(Debit) or nameof(HoldEscrow) or nameof(SubmitCredit) or nameof(ReleaseEscrow) or nameof(Refund);
        if (frozen && moneyIntent)
        {
            throw new InvalidDataException("A frozen decision cannot enqueue a money intent.");
        }
        if (frozen)
        {
            using var freeze = Sqlite.Command(db, "UPDATE outbox SET status='blocked' WHERE transfer=$transfer AND status='pending'",
                ("$transfer", transfer));
            freeze.Transaction = transaction;
            freeze.ExecuteNonQuery();
        }

        if (moneyIntent)
        {
            var key = observation.ActionData.GetProperty("Key").GetString();
            if (key is null || key.LastIndexOf(':') != transfer.Length || !key.StartsWith(transfer + ":", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Money intent needs an operation key for this transfer.");
            }
            using var enqueue = Sqlite.Command(db, "INSERT INTO outbox VALUES($key,$transfer,$sequence,$kind,$payload,'pending')",
                ("$key", key), ("$transfer", transfer), ("$sequence", entry.Sequence), ("$kind", observation.Action),
                ("$payload", observation.ActionData.GetRawText()));
            enqueue.Transaction = transaction;
            enqueue.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public DispatchIntent? Next()
    {
        using var db = Sqlite.Open(path);
        using var select = Sqlite.Command(db, "SELECT key,kind,payload FROM outbox WHERE status='pending' ORDER BY transfer,sequence LIMIT 1");
        using var rows = select.ExecuteReader();
        return rows.Read() ? new(rows.GetString(0), rows.GetString(1), rows.GetString(2)) : null;
    }

    public bool DispatchOne(SimulatedPartner partner)
    {
        if (Next() is not { } intent) { return false; }
        partner.Deliver(intent);
        using var db = Sqlite.Open(path);
        using var acknowledge = Sqlite.Command(db, "UPDATE outbox SET status='delivered' WHERE key=$key AND status='pending'", ("$key", intent.Key));
        acknowledge.ExecuteNonQuery();
        return true;
    }

    public JournalEntry ReadLatest(string transfer)
    {
        using var db = Sqlite.Open(path);
        using var select = Sqlite.Command(db, "SELECT entry FROM journal WHERE transfer=$transfer ORDER BY sequence DESC LIMIT 1", ("$transfer", transfer));
        return select.ExecuteScalar() is string json && JsonSerializer.Deserialize<JournalEntry>(json) is { } entry &&
            EvidenceJournal.HasValidHash(entry)
            ? entry
            : throw new InvalidDataException("No valid persisted decision for the transfer.");
    }

    private static void CheckPredecessor(SqliteConnection db, SqliteTransaction transaction, string transfer, JournalEntry incoming)
    {
        using var query = Sqlite.Command(db, "SELECT sequence,digest,observed_ticks FROM journal WHERE transfer=$transfer ORDER BY sequence DESC LIMIT 1",
            ("$transfer", transfer));
        query.Transaction = transaction;
        using var rows = query.ExecuteReader();
        var current = rows.Read()
            ? (Sequence: rows.GetInt32(0), Digest: rows.GetString(1), Observed: rows.GetInt64(2))
            : (Sequence: 0, Digest: EvidenceJournal.Genesis, Observed: 0L);
        if (incoming.Sequence != current.Sequence + 1 || incoming.PreviousSha256 != current.Digest ||
            incoming.Observation.Time.UtcTicks < current.Observed)
        {
            throw new InvalidDataException("Decision does not extend the committed journal.");
        }
    }
}

public sealed class SimulatedPartner
{
    private readonly string path;

    public SimulatedPartner(string path)
    {
        this.path = path;
        using var db = Sqlite.Open(path);
        Sqlite.Execute(db, "CREATE TABLE IF NOT EXISTS effects (key TEXT PRIMARY KEY, kind TEXT NOT NULL, payload TEXT NOT NULL, attempts INTEGER NOT NULL)");
    }

    public void Deliver(DispatchIntent intent)
    {
        using var db = Sqlite.Open(path);
        using var apply = Sqlite.Command(db, """
            INSERT INTO effects VALUES($key,$kind,$payload,1)
            ON CONFLICT(key) DO UPDATE SET attempts=attempts+1
            WHERE effects.kind=excluded.kind AND effects.payload=excluded.payload
            """, ("$key", intent.Key), ("$kind", intent.Kind), ("$payload", intent.Payload));
        if (apply.ExecuteNonQuery() != 1)
        {
            throw new InvalidDataException("Operation key conflicts with an already applied intent.");
        }
    }

    public DeliveryCount Count(string key)
    {
        using var db = Sqlite.Open(path);
        using var query = Sqlite.Command(db, "SELECT COALESCE(SUM(attempts),0), COUNT(*) FROM effects WHERE key=$key", ("$key", key));
        using var rows = query.ExecuteReader();
        if (!rows.Read()) { throw new InvalidDataException("Missing partner count."); }
        return new(rows.GetInt32(0), rows.GetInt32(1));
    }
}

internal static class Sqlite
{
    internal static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, ForeignKeys = true
        }.ToString());
        try
        {
            db.Open();
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    internal static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object Value)[] values)
    {
        var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values) { command.Parameters.AddWithValue(name, value); }
        return command;
    }

    internal static void Execute(SqliteConnection db, string sql)
    {
        using var command = Command(db, sql);
        command.ExecuteNonQuery();
    }
}
