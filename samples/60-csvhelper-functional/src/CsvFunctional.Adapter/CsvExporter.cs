using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CsvFunctional.Domain;
using CsvHelper;
using CsvHelper.Configuration;

namespace CsvFunctional.Adapter;

// Emits records to a borrowed writer. Completion flushes it, but never closes it.
// Producer exceptions propagate; cancellation and active transport errors are typed.
public sealed class CsvExporter
{
    private readonly CultureInfo culture;
    private readonly int maximumRows;

    public CsvExporter(CultureInfo culture, int maximumRows = 10_000)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        this.culture = CultureInfo.ReadOnly((CultureInfo)culture.Clone());
        this.maximumRows = maximumRows;
    }

    public async IAsyncEnumerable<ExportEvent> WriteAsync(IAsyncEnumerable<ImportedRecord> source, TextWriter output, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        using var borrowed = new BorrowedWriter(output, cancellationToken);
        var csv = new CsvWriter(borrowed, new CsvConfiguration(culture) { Delimiter = ",", NewLine = "\r\n", ShouldQuote = _ => true }, leaveOpen: true);
        try
        {
            var header = await WriteStepAsync(csv, RowDecisions.Header, false, cancellationToken).ConfigureAwait(false);
            if (header is WriteCancelled) { yield return new ExportCancelled(0); yield break; }
            if (header is WriteFailed) { yield return new ExportFailure(0, ExportFailureKind.Output); yield break; }
            var count = 0;
            await using var iterator = source.GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                var cancelled = false;
                bool hasNext;
                try { hasNext = await iterator.MoveNextAsync().ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { cancelled = true; hasNext = false; }
                if (cancelled || cancellationToken.IsCancellationRequested) { yield return new ExportCancelled(count + 1); yield break; }
                if (!hasNext) { break; }
                count++;
                if (count > maximumRows) { yield return new ExportFailure(count, ExportFailureKind.RowLimit); yield break; }
                var formatted = ExportDecisions.Format(iterator.Current, culture);
                if (formatted is OversizedNote) { yield return new ExportFailure(count, ExportFailureKind.NoteLimit); yield break; }
                if (formatted is FormattedRow row)
                {
                    var step = await WriteStepAsync(csv, row.Fields, false, cancellationToken).ConfigureAwait(false);
                    if (step is WriteCancelled) { yield return new ExportCancelled(count); yield break; }
                    if (step is WriteFailed) { yield return new ExportFailure(count, ExportFailureKind.Output); yield break; }
                    yield return new ExportedRow(count);
                }
            }
            var final = await WriteStepAsync(csv, [], true, cancellationToken).ConfigureAwait(false);
            if (final is WriteCancelled) { yield return new ExportCancelled(count + 1); }
            if (final is WriteFailed) { yield return new ExportFailure(count + 1, ExportFailureKind.Output); }
        }
        finally
        {
            // Never retry a pending buffer after failure, cancellation or early
            // abandonment. Successful completion already flushed it explicitly.
            borrowed.Disconnect();
            csv.Dispose();
        }
    }

    private static async ValueTask<WriteStep> WriteStepAsync(CsvWriter csv, ImmutableArray<string> fields, bool flush, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (flush) { await csv.FlushAsync().ConfigureAwait(false); }
            else
            {
                foreach (var field in fields) { csv.WriteField(field); }
                await csv.NextRecordAsync().ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new WriteSucceeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new WriteCancelled(); }
        catch (IOException) { return new WriteFailed(); }
        catch (CsvHelperException) { return new WriteFailed(); }
    }

    private readonly record struct WriteSucceeded;
    private readonly record struct WriteFailed;
    private readonly record struct WriteCancelled;
    private readonly union WriteStep(WriteSucceeded, WriteFailed, WriteCancelled);

    private sealed class BorrowedWriter(TextWriter inner, CancellationToken token) : TextWriter
    {
        private bool disconnected;
        public override Encoding Encoding => inner.Encoding;
        public void Disconnect() => disconnected = true;
        public override void Write(char[] buffer, int index, int count) { if (!disconnected) { inner.Write(buffer, index, count); } }
        public override void Flush() { if (!disconnected) { inner.Flush(); } }
        public override Task WriteAsync(char[] buffer, int index, int count) => disconnected ? Task.CompletedTask : inner.WriteAsync(buffer.AsMemory(index, count), token);
        public override Task FlushAsync() => disconnected ? Task.CompletedTask : inner.FlushAsync(token);
    }
}
