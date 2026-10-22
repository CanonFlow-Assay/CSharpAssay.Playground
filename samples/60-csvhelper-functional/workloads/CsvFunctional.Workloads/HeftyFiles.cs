using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CsvFunctional.Adapter;
using CsvFunctional.Domain;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;

namespace CsvFunctional.Workloads;

public enum HeftyPipeline { Original, ResilientOriginal, Assayed }
public enum HeftyFailure { None, TypeConversion, Schema, Transport, Cancelled }
public sealed record HeftySpec(string Id, int Rows, int NoteCharacters, bool Unicode, string Culture, int InvalidEvery);
public sealed record HeftyDigest(StreamDigest Totals, string OutputSha256, HeftyFailure Failure);
public sealed record HeftyFixture(HeftySpec Specification, string FileName, long FileBytes, string FileSha256, HeftyDigest Expected, HeftyDigest OriginalExpected);
public sealed record HeftyManifest(string Schema, ImmutableArray<HeftyFixture> Fixtures);

public static class HeftyFiles
{
    public static async Task<HeftyFixture> GenerateAsync(string directory, HeftySpec specification, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(specification.Id);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(specification.Id.Length, 48);
        ArgumentOutOfRangeException.ThrowIfNegative(specification.Rows);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(specification.Rows, 1_000_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(specification.NoteCharacters, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(specification.NoteCharacters, 2048);
        ArgumentOutOfRangeException.ThrowIfNegative(specification.InvalidEvery);
        ArgumentOutOfRangeException.ThrowIfEqual(specification.Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'), false);
        Directory.CreateDirectory(directory);
        var culture = CultureInfo.GetCultureInfo(specification.Culture);
        var filename = specification.Id + ".csv";
        var path = Path.Combine(directory, filename);
        var pending = path + ".partial";
        using var sink = new HeftySink();
        using var prefix = new HeftySink();
        await using (var output = new StreamWriter(pending, append: false, new UTF8Encoding(false, true), 65536))
        {
            await output.WriteAsync("Id,Amount,Active,Note\r\n".AsMemory(), cancellationToken).ConfigureAwait(false);
            for (var id = 1; id <= specification.Rows; id++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var note = Note(id, specification.NoteCharacters, specification.Unicode);
                var invalid = specification.InvalidEvery > 0 && id % specification.InvalidEvery == 0;
                var amount = (id / 100m).ToString("F2", culture);
                var record = invalid ? $"bad,bad,bad,{Quote(note)}\r\n"
                    : $"{id.ToString(CultureInfo.InvariantCulture)},{Quote(amount)},{(id % 2 == 0 ? "1" : "false")},{Quote(note)}\r\n";
                await output.WriteAsync(record.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (specification.InvalidEvery > 0 && id == specification.InvalidEvery) { prefix.Fail(id, HeftyFailure.TypeConversion); }
                else if (specification.InvalidEvery == 0 || id < specification.InvalidEvery) { prefix.Add(id, id, id / 100m, id % 2 == 0, note); }
                if (invalid) { sink.Reject(id, 7); }
                else { sink.Add(id, id, id / 100m, id % 2 == 0, note); }
            }
        }
        File.Move(pending, path, overwrite: true);
        await using var input = File.OpenRead(path);
        var checksum = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
        return new(specification, filename, input.Length, checksum, sink.Finish(), prefix.Finish());
    }

    public static string Note(int id, int length, bool unicode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 16);
        return string.Create(length, (id, unicode), static (span, state) =>
        {
            var prefix = "id=" + state.id.ToString(CultureInfo.InvariantCulture) + "|";
            prefix.AsSpan().CopyTo(span);
            var pattern = state.unicode ? "Ω,客户 \"quoted\"\nनमस्ते " : "x";
            for (var index = prefix.Length; index < span.Length; index++)
            {
                span[index] = pattern[(index - prefix.Length) % pattern.Length];
            }
        });
    }

    public static async Task<HeftyDigest> RunAsync(string path, HeftySpec specification, HeftyPipeline pipeline, CancellationToken cancellationToken)
    {
        using var input = new StreamReader(new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            BufferSize = 65536
        }), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: 65536);
        using var sink = new HeftySink();
        var culture = CultureInfo.GetCultureInfo(specification.Culture);
        if (pipeline == HeftyPipeline.Assayed)
        {
            await foreach (var item in new CsvImporter(culture, Math.Max(1, specification.Rows)).ReadAsync(input, cancellationToken).ConfigureAwait(false))
            {
                sink.Observe(item);
            }
            return sink.Finish();
        }
        var configuration = new CsvConfiguration(culture)
        {
            Delimiter = ",",
            IgnoreBlankLines = false,
            ExceptionMessagesContainRawData = false,
            MaxFieldSize = 4096
        };
        using var csv = new CsvReader(input, configuration);
        var recordNumber = 0;
        if (pipeline == HeftyPipeline.Original)
        {
            try
            {
                await foreach (var row in csv.GetRecordsAsync<DirectRecord>(cancellationToken).ConfigureAwait(false))
                {
                    recordNumber++;
                    sink.Add(recordNumber, row.Id, row.Amount, row.Active, row.Note);
                }
            }
            catch (TypeConverterException) { sink.Fail(recordNumber + 1, HeftyFailure.TypeConversion); }
        }
        else
        {
            if (!await csv.ReadAsync().ConfigureAwait(false)) { sink.Fail(0, HeftyFailure.Schema); return sink.Finish(); }
            csv.ReadHeader();
            if (csv.HeaderRecord is not { } header || !header.SequenceEqual(RowDecisions.Header))
            {
                sink.Fail(0, HeftyFailure.Schema);
                return sink.Finish();
            }
            while (await csv.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                recordNumber++;
                if (csv.Parser.Count != 4) { sink.Reject(recordNumber, 8); continue; }
                var idValid = csv.TryGetField<int>(0, out var id);
                var amountValid = csv.TryGetField<decimal>(1, out var amount);
                var activeValid = csv.TryGetField<bool>(2, out var active);
                var note = csv.GetField(3);
                var mask = (idValid ? 0 : 1) | (amountValid ? 0 : 2) | (activeValid ? 0 : 4) | (note is null ? 8 : 0);
                if (mask != 0) { sink.Reject(recordNumber, mask); }
                else if (note is { } value) { sink.Add(recordNumber, id, amount, active, value); }
            }
        }
        return sink.Finish();
    }

    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private sealed class HeftySink : IDisposable
    {
        private readonly DigestSink totals = new();
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly byte[] bytes = new byte[8192];
        private HeftyFailure failure;
        private int failureRecord;

        public void Add(int row, int id, decimal amount, bool active, string note)
        {
            totals.Add(row, id, amount, active, note);
            var length = Encoding.UTF8.GetBytes(note.AsSpan(), bytes);
            Span<byte> header = stackalloc byte[22];
            header[0] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(header[1..], row);
            BinaryPrimitives.WriteInt32LittleEndian(header[5..], id);
            BinaryPrimitives.WriteInt64LittleEndian(header[9..], decimal.ToInt64(amount * 100m));
            header[17] = active ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(header[18..], length);
            hash.AppendData(header);
            hash.AppendData(bytes.AsSpan(0, length));
        }

        public void Reject(int row, int mask)
        {
            totals.Reject(BitOperations.PopCount((uint)mask));
            Span<byte> data = stackalloc byte[9];
            data[0] = 2;
            BinaryPrimitives.WriteInt32LittleEndian(data[1..], row);
            BinaryPrimitives.WriteInt32LittleEndian(data[5..], mask);
            hash.AppendData(data);
        }

        public void Fail(int row, HeftyFailure kind) { failure = kind; failureRecord = row; }
        public void Observe(ImportEvent item)
        {
            switch (item)
            {
                case AcceptedRow row: Add(row.RecordNumber, row.Value.Id, row.Value.Amount, row.Value.Active, row.Value.Note); break;
                case RejectedRow row:
                    var mask = 0;
                    foreach (var issue in row.Issues)
                    {
                        mask |= issue.Kind switch { CsvIssueKind.InvalidInteger => 1, CsvIssueKind.InvalidDecimal => 2, CsvIssueKind.InvalidBoolean => 4, CsvIssueKind.WrongFieldCount => 8, _ => 16 };
                    }
                    Reject(row.RecordNumber, mask);
                    break;
                case ImportFailure row: Fail(row.RecordNumber, HeftyFailure.Transport); break;
                case ImportCancelled row: Fail(row.RecordNumber, HeftyFailure.Cancelled); break;
            }
        }
        public HeftyDigest Finish()
        {
            Span<byte> tail = stackalloc byte[6];
            tail[0] = 3;
            tail[1] = (byte)failure;
            BinaryPrimitives.WriteInt32LittleEndian(tail[2..], failureRecord);
            hash.AppendData(tail);
            var snapshot = totals.Snapshot();
            snapshot = snapshot with { Stop = failure == HeftyFailure.None ? StreamStop.Completed : failure == HeftyFailure.Cancelled ? StreamStop.Cancelled : StreamStop.Failed, StopRecord = failureRecord };
            return new(snapshot, Convert.ToHexString(hash.GetHashAndReset()), failure);
        }
        public void Dispose() => hash.Dispose();
    }
}
