using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using CsvFunctional.Domain;
using CsvHelper;
using CsvHelper.Configuration;

namespace CsvFunctional.Adapter;

// Streaming effects stay here; the caller owns the TextReader. Record numbers
// count logical data records, so quoted newlines do not change their identity.
public sealed class CsvImporter
{
    private readonly CultureInfo culture;
    private readonly int maximumRows;

    public CsvImporter(CultureInfo culture, int maximumRows = 10_000)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        this.culture = CultureInfo.ReadOnly((CultureInfo)culture.Clone());
        this.maximumRows = maximumRows;
    }

    public async IAsyncEnumerable<ImportEvent> ReadAsync(TextReader input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            HasHeaderRecord = false,
            IgnoreBlankLines = false,
            ExceptionMessagesContainRawData = false,
            MaxFieldSize = 4096
        };
        using var reader = new CsvReader(new CancellationReader(input, cancellationToken), configuration, leaveOpen: true);
        var header = await ReadStepAsync(reader, cancellationToken).ConfigureAwait(false);
        switch (header)
        {
            case ReadRow row when row.Fields.SequenceEqual(RowDecisions.Header): break;
            case ReadCancelled: yield return new ImportCancelled(0); yield break;
            case ReadFailure failure: yield return new ImportFailure(0, failure.Kind); yield break;
            case EndOfInput:
            case ReadRow: yield return new ImportFailure(0, ImportFailureKind.Schema); yield break;
        }
        var recordNumber = 0;
        while (true)
        {
            var step = await ReadStepAsync(reader, cancellationToken).ConfigureAwait(false);
            if (step is not EndOfInput) { recordNumber++; }
            switch (step)
            {
                case EndOfInput: yield break;
                case ReadCancelled: yield return new ImportCancelled(recordNumber); yield break;
                case ReadFailure failure: yield return new ImportFailure(recordNumber, failure.Kind); yield break;
                case ReadRow when recordNumber > maximumRows:
                    yield return new ImportFailure(recordNumber, ImportFailureKind.RowLimit); yield break;
                case ReadRow row:
                    yield return RowDecisions.Convert(recordNumber, row.Fields, culture) switch
                    {
                        AcceptedRow accepted => accepted,
                        RejectedRow rejected => rejected
                    };
                    break;
            }
        }
    }

    private static async ValueTask<ReadStep> ReadStepAsync(CsvReader reader, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hasRow = await reader.ReadAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!hasRow) { return new EndOfInput(); }
            ImmutableArray<string> fields = [.. reader.Parser.Record ?? []];
            // Defend the declared character limit at EOF as well as during
            // parser buffering. Test both 4096 and 4097 without a final newline.
            return fields.Any(field => field.Length > 4096)
                ? new ReadFailure(ImportFailureKind.FieldLimit) : new ReadRow(fields);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new ReadCancelled(); }
        catch (MaxFieldSizeException) { return new ReadFailure(ImportFailureKind.FieldLimit); }
        catch (CsvHelperException) { return new ReadFailure(ImportFailureKind.MalformedCsv); }
        catch (IOException) { return new ReadFailure(ImportFailureKind.InputOutput); }
    }

    private readonly union ReadStep(ReadRow, EndOfInput, ReadFailure, ReadCancelled);
    private sealed record ReadRow(ImmutableArray<string> Fields);
    private readonly record struct EndOfInput;
    private sealed record ReadFailure(ImportFailureKind Kind);
    private readonly record struct ReadCancelled;

    private sealed class CancellationReader(TextReader inner, CancellationToken cancellationToken) : TextReader
    {
        public override Task<int> ReadAsync(char[] buffer, int index, int count) =>
            inner.ReadAsync(buffer.AsMemory(index, count), cancellationToken).AsTask();
    }
}
