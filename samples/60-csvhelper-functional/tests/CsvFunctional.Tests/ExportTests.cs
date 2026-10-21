using System.Globalization;
using CsvFunctional.Adapter;
using CsvFunctional.Domain;
using Xunit;

namespace CsvFunctional.Tests;

public sealed class ExportTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly ImportedRecord Example = new(7, 123.4567890123456789012345678m, true, "Ω, \"note\"\n客户");

    [Theory]
    [InlineData("")]
    [InlineData("de-DE")]
    public void FormattingPreservesFullPrecisionAndExplicitCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var value = Assert.IsType<FormattedRow>(ExportDecisions.Format(Example, culture).Value);
        Assert.Equal(Example.Amount.ToString(culture), value.Fields[1]);
        Assert.Equal("True", value.Fields[2]);
        Assert.Equal(Example.Note, value.Fields[3]);
    }

    [Fact]
    public async Task EmptySourceWritesExactQuotedHeaderAndLeavesWriterOpen()
    {
        using var output = new StringWriter(Invariant);
        Assert.Empty(await Collect(new CsvExporter(Invariant).WriteAsync(Records(), output, TestContext.Current.CancellationToken)));
        Assert.Equal("\"Id\",\"Amount\",\"Active\",\"Note\"\r\n", output.ToString());
        output.Write("still open");
    }

    [Theory]
    [InlineData("")]
    [InlineData("de-DE")]
    public async Task QuotedUnicodeAndFullDecimalsRoundTrip(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        using var output = new StringWriter(Invariant);
        var exported = Assert.Single(await Collect(new CsvExporter(culture).WriteAsync(Records(Example), output, TestContext.Current.CancellationToken)));
        Assert.Equal(1, Assert.IsType<ExportedRow>(exported.Value).RecordNumber);
        Assert.Contains("\"Ω, \"\"note\"\"\n客户\"", output.ToString(), StringComparison.Ordinal);
        using var input = new StringReader(output.ToString());
        var imported = await ReadOne(input, culture);
        Assert.Equal(Example, imported.Value);
    }

    [Theory]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public async Task NoteLimitHasAnExplicitBoundary(int length, bool accepted)
    {
        using var output = new StringWriter(Invariant);
        var result = Assert.Single(await Collect(new CsvExporter(Invariant).WriteAsync(Records(Example with { Note = new('"', length) }), output, TestContext.Current.CancellationToken)));
        if (accepted)
        {
            Assert.IsType<ExportedRow>(result.Value);
            using var input = new StringReader(output.ToString());
            Assert.Equal(new('"', length), (await ReadOne(input, Invariant)).Value.Note);
        }
        else { Assert.Equal(ExportFailureKind.NoteLimit, Assert.IsType<ExportFailure>(result.Value).Kind); }
    }

    [Fact]
    public async Task RowLimitAcceptsExactlyTheConfiguredCount()
    {
        using var output = new StringWriter(Invariant);
        var result = await Collect(new CsvExporter(Invariant, 1).WriteAsync(Records(Example, Example), output, TestContext.Current.CancellationToken));
        Assert.Equal(2, result.Count);
        Assert.Equal(1, Assert.IsType<ExportedRow>(result[0].Value).RecordNumber);
        Assert.Equal(new ExportFailure(2, ExportFailureKind.RowLimit), Assert.IsType<ExportFailure>(result[1].Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveLimitsAreRejected(int maximum) => Assert.Throws<ArgumentOutOfRangeException>(() => new CsvExporter(Invariant, maximum));

    [Fact]
    public async Task PreCancellationWritesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new StringWriter(Invariant);
        var result = Assert.Single(await Collect(new CsvExporter(Invariant).WriteAsync(Records(Example), output, cancellation.Token)));
        Assert.Equal(0, Assert.IsType<ExportCancelled>(result.Value).RecordNumber);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task CancellationAfterProgressHasOneTerminalOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        using var output = new StringWriter(Invariant);
        await using var iterator = new CsvExporter(Invariant).WriteAsync(Records(Example, Example), output, cancellation.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await iterator.MoveNextAsync());
        cancellation.Cancel();
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(2, Assert.IsType<ExportCancelled>(iterator.Current.Value).RecordNumber);
        Assert.False(await iterator.MoveNextAsync());
    }

    [Fact]
    public async Task OutputFailureIsTypedAndDoesNotRetryOnDisposal()
    {
        using var output = new FailedWriter();
        var result = Assert.Single(await Collect(new CsvExporter(Invariant).WriteAsync(Records(Example), output, TestContext.Current.CancellationToken)));
        Assert.Equal(new ExportFailure(0, ExportFailureKind.Output), Assert.IsType<ExportFailure>(result.Value));
        Assert.Equal(1, output.Attempts);
        output.Write("caller still owns this");
    }

    [Fact]
    public async Task FinalFlushFailureIsReportedAfterWrittenRows()
    {
        using var output = new FailedFlushWriter();
        var result = await Collect(new CsvExporter(Invariant).WriteAsync(Records(Example), output, TestContext.Current.CancellationToken));
        Assert.Equal(2, result.Count);
        Assert.IsType<ExportedRow>(result[0].Value);
        Assert.Equal(new ExportFailure(2, ExportFailureKind.Output), Assert.IsType<ExportFailure>(result[1].Value));
    }

    [Fact]
    public async Task CancellationInterruptsAnInFlightWrite()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var output = new BlockingWriter();
        await using var iterator = new CsvExporter(Invariant).WriteAsync(Records(Example), output, cancellation.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var pending = iterator.MoveNextAsync().AsTask();
        await output.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.IsType<ExportCancelled>(iterator.Current.Value);
    }

    [Fact]
    public async Task EarlyDisposalLeavesTheWriterOpen()
    {
        using var output = new StringWriter(Invariant);
        await using (var iterator = new CsvExporter(Invariant).WriteAsync(Records(Example, Example), output, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            Assert.True(await iterator.MoveNextAsync());
        }
        output.Write("still open");
        Assert.EndsWith("still open", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProducerExceptionsRemainCallerExceptions()
    {
        using var output = new StringWriter(Invariant);
        await Assert.ThrowsAsync<IOException>(async () => { await Collect(new CsvExporter(Invariant).WriteAsync(FailedSource(), output, TestContext.Current.CancellationToken)); });
    }

    private static async IAsyncEnumerable<ImportedRecord> Records(params ImportedRecord[] rows)
    {
        await Task.CompletedTask;
        foreach (var row in rows) { yield return row; }
    }
    private static async IAsyncEnumerable<ImportedRecord> FailedSource()
    {
        await Task.CompletedTask;
        yield return Example;
        throw new IOException("producer failure");
    }
    private static async Task<List<ExportEvent>> Collect(IAsyncEnumerable<ExportEvent> events)
    {
        var result = new List<ExportEvent>();
        await foreach (var item in events) { result.Add(item); }
        return result;
    }
    private static async Task<AcceptedRow> ReadOne(TextReader input, CultureInfo culture)
    {
        await using var iterator = new CsvImporter(culture).ReadAsync(input, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await iterator.MoveNextAsync());
        var row = Assert.IsType<AcceptedRow>(iterator.Current.Value);
        Assert.False(await iterator.MoveNextAsync());
        return row;
    }
    private sealed class FailedWriter : StringWriter
    {
        public int Attempts { get; private set; }
        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return Task.FromException(new IOException("private output detail"));
        }
    }
    private sealed class FailedFlushWriter : StringWriter
    {
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.FromException(new IOException("private flush detail"));
    }
    private sealed class BlockingWriter : StringWriter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
