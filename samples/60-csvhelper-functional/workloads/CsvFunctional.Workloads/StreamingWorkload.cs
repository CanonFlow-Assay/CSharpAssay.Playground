using System.Globalization;
using CsvFunctional.Adapter;
using CsvFunctional.Domain;
using CsvHelper;
using CsvHelper.Configuration;

namespace CsvFunctional.Workloads;

public sealed record StreamSpec(int Rows, int ChunkSize, int InvalidEvery, bool Multiline, string CultureName);
public enum StreamStop { Completed, Failed, Cancelled }
public sealed record StreamDigest(int Accepted, int Rejected, long IdSum, decimal AmountSum, int ActiveCount,
    long NoteCharacters, ulong Fingerprint, int IssueCount, StreamStop Stop, int StopRecord);

// A generated TextReader avoids a pre-materialized input file/string. The source
// records read-ahead independently from the consumer's aggregate output.
public sealed class GeneratedCsvReader : TextReader
{
    private readonly StreamSpec specification;
    private readonly CultureInfo culture;
    private string pending = string.Empty;
    private int offset;
    private int nextRecord;

    public GeneratedCsvReader(StreamSpec specification)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(specification.Rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(specification.ChunkSize);
        ArgumentOutOfRangeException.ThrowIfNegative(specification.InvalidEvery);
        this.specification = specification;
        culture = CultureInfo.GetCultureInfo(specification.CultureName);
    }

    public int GeneratedRecords { get; private set; }
    public long CharactersRead { get; private set; }
    public int ReadCalls { get; private set; }
    public bool IsDisposed { get; private set; }

    public override Task<int> ReadAsync(char[] buffer, int index, int count) =>
        ReadAsync(buffer.AsMemory(index, count)).AsTask();

    public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        ReadCalls++;
        var written = 0;
        var limit = Math.Min(buffer.Length, specification.ChunkSize);
        while (written < limit)
        {
            if (offset == pending.Length)
            {
                if (nextRecord > specification.Rows) { break; }
                pending = nextRecord == 0 ? "Id,Amount,Active,Note\r\n" : Encode(nextRecord);
                if (nextRecord > 0) { GeneratedRecords++; }
                nextRecord++;
                offset = 0;
            }
            var length = Math.Min(limit - written, pending.Length - offset);
            pending.AsSpan(offset, length).CopyTo(buffer.Span[written..]);
            offset += length;
            written += length;
        }
        CharactersRead += written;
        return ValueTask.FromResult(written);
    }

    public static string Note(int id, bool multiline) => multiline && id % 7 == 0
        ? $"note {id}, \"quoted\"\ncontinued" : $"note {id}";

    private string Encode(int id)
    {
        var invalid = specification.InvalidEvery > 0 && id % specification.InvalidEvery == 0;
        var amount = (id / 100m).ToString("F2", culture);
        var note = Note(id, specification.Multiline).Replace("\"", "\"\"", StringComparison.Ordinal);
        return invalid ? $"bad,bad,bad,\"{note}\"\r\n"
            : $"{id.ToString(CultureInfo.InvariantCulture)},\"{amount}\",{(id % 2 == 0 ? "1" : "false")},\"{note}\"\r\n";
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}

public static class StreamRunner
{
    public static async Task<StreamDigest> FunctionalAsync(StreamSpec specification, CancellationToken cancellationToken)
    {
        using var input = new GeneratedCsvReader(specification);
        var importer = new CsvImporter(CultureInfo.GetCultureInfo(specification.CultureName), Math.Max(1, specification.Rows));
        var sink = new DigestSink();
        await foreach (var item in importer.ReadAsync(input, cancellationToken).ConfigureAwait(false))
        {
            sink.Observe(item);
        }
        return sink.Snapshot();
    }

    public static async Task<StreamDigest> DirectAsync(StreamSpec specification, CancellationToken cancellationToken)
    {
        // The comparison is intentionally valid-only. Direct mapping throws on
        // conversion failure and does not implement the adapter's event contract.
        ArgumentOutOfRangeException.ThrowIfNotEqual(specification.InvalidEvery, 0);
        using var input = new GeneratedCsvReader(specification);
        var configuration = new CsvConfiguration(CultureInfo.GetCultureInfo(specification.CultureName))
        {
            Delimiter = ",",
            IgnoreBlankLines = false,
            ExceptionMessagesContainRawData = false,
            MaxFieldSize = 4096
        };
        using var csv = new CsvReader(input, configuration);
        var sink = new DigestSink();
        var row = 0;
        await foreach (var record in csv.GetRecordsAsync<DirectRecord>(cancellationToken).ConfigureAwait(false))
        {
            row++;
            sink.Add(row, record.Id, record.Amount, record.Active, record.Note);
        }
        return sink.Snapshot();
    }

    public static StreamDigest Expected(StreamSpec specification)
    {
        var sink = new DigestSink();
        for (var id = 1; id <= specification.Rows; id++)
        {
            if (specification.InvalidEvery > 0 && id % specification.InvalidEvery == 0)
            {
                sink.Reject(3);
            }
            else
            {
                sink.Add(id, id, id / 100m, id % 2 == 0, GeneratedCsvReader.Note(id, specification.Multiline));
            }
        }
        return sink.Snapshot();
    }
}

// Public DTO deliberately lives in the workload shell; CsvHelper maps it using
// its ordinary published API. No domain project depends on this representation.
public sealed record DirectRecord(int Id, decimal Amount, bool Active, string Note);

public sealed class DigestSink
{
    private int accepted;
    private int rejected;
    private long idSum;
    private decimal amountSum;
    private int activeCount;
    private long noteCharacters;
    private ulong fingerprint = 14695981039346656037UL;
    private int issueCount;
    private StreamStop stop;
    private int stopRecord;

    public void Observe(ImportEvent item)
    {
        switch (item)
        {
            case AcceptedRow row: Add(row.RecordNumber, row.Value.Id, row.Value.Amount, row.Value.Active, row.Value.Note); break;
            case RejectedRow row: Reject(row.Issues.Length); break;
            case ImportFailure failure: stop = StreamStop.Failed; stopRecord = failure.RecordNumber; break;
            case ImportCancelled cancelled: stop = StreamStop.Cancelled; stopRecord = cancelled.RecordNumber; break;
        }
    }

    public void Add(int recordNumber, int id, decimal amount, bool active, string note)
    {
        accepted++;
        idSum += id;
        amountSum += amount;
        if (active) { activeCount++; }
        noteCharacters += note.Length;
        Mix((ulong)recordNumber);
        Mix((ulong)(uint)id);
        // Generated amounts have exactly two fractional digits; hash numeric
        // cents so decimal scale differences do not change an equal value.
        Mix((ulong)decimal.ToInt64(amount * 100m));
        Mix(active ? 1UL : 0UL);
        foreach (var character in note) { Mix(character); }
    }

    public void Reject(int issues) { rejected++; issueCount += issues; }
    public StreamDigest Snapshot() => new(accepted, rejected, idSum, amountSum, activeCount, noteCharacters, fingerprint, issueCount, stop, stopRecord);
    private void Mix(ulong value) { fingerprint = unchecked((fingerprint ^ value) * 1099511628211UL); }
}
