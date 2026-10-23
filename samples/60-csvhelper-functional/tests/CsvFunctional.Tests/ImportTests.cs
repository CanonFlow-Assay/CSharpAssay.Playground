using System.Globalization;
using CsvFunctional.Adapter;
using CsvFunctional.Domain;
using Xunit;

namespace CsvFunctional.Tests;

public sealed class ImportTests
{
    private const string Header = "Id,Amount,Active,Note\r\n";

    [Theory]
    [InlineData("\"a,b\"", "a,b")]
    [InlineData("\"a\"\"b\"", "a\"b")]
    [InlineData("\"a\r\nb\"", "a\r\nb")]
    [InlineData("", "")]
    public async Task PreservesCsvSyntax(string encoded, string expected)
    {
        var row = Assert.IsType<AcceptedRow>(Assert.Single(await Collect(Header + "1,2,true," + encoded + "\r\n")).Value);
        Assert.Equal(expected, row.Value.Note);
        Assert.Equal(1, row.RecordNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Id,Amount,Active\n")]
    [InlineData("Id,Id,Active,Note\n")]
    [InlineData("id,Amount,Active,Note\n")]
    [InlineData("Amount,Id,Active,Note\n")]
    public async Task RejectsWrongSchema(string text) =>
        Assert.Equal(new ImportFailure(0, ImportFailureKind.Schema), Assert.Single(await Collect(text)).Value);

    [Fact]
    public async Task HeaderOnlyIsACompletedEmptyStream() => Assert.Empty(await Collect(Header));

    [Fact]
    public async Task RejectedRowDoesNotLoseFollowingValidRow()
    {
        var events = await Collect(Header + "bad,bad,bad,secret\n2,3,false,last\n");
        var rejected = Assert.IsType<RejectedRow>(events[0].Value);
        Assert.Equal(3, rejected.Issues.Length);
        var accepted = Assert.IsType<AcceptedRow>(events[1].Value);
        Assert.Equal(2, accepted.RecordNumber);
        Assert.Equal(new ImportedRecord(2, 3m, false, "last"), accepted.Value);
        Assert.Equal(2, events.Count);
    }

    [Theory]
    [InlineData("1,2,true")]
    [InlineData("1,2,true,note,extra")]
    [InlineData("")]
    public async Task MissingExtraAndBlankRecordsAreRejected(string record)
    {
        var events = await Collect(Header + record + "\n");
        Assert.Equal(CsvIssueKind.WrongFieldCount, Assert.Single(Assert.IsType<RejectedRow>(Assert.Single(events).Value).Issues).Kind);
    }

    [Fact]
    public async Task LogicalNumbersIgnoreQuotedNewlinesAndSnapshotsStayIndependent()
    {
        var events = await Collect(Header + "1,1,true,\"first\nline\"\n2,2,false,last\n");
        var first = Assert.IsType<AcceptedRow>(events[0].Value);
        var second = Assert.IsType<AcceptedRow>(events[1].Value);
        Assert.Equal((1, "first\nline"), (first.RecordNumber, first.Value.Note));
        Assert.Equal((2, "last"), (second.RecordNumber, second.Value.Note));
    }

    [Fact]
    public async Task ExplicitCultureParsesQuotedAmount()
    {
        using var input = new StringReader(Header + "1,\"1.234,50\",true,note\n");
        var values = new List<ImportEvent>();
        await foreach (var item in new CsvImporter(CultureInfo.GetCultureInfo("de-DE")).ReadAsync(input, TestContext.Current.CancellationToken)) { values.Add(item); }
        Assert.Equal(1234.50m, Assert.IsType<AcceptedRow>(Assert.Single(values).Value).Value.Amount);
    }

    [Fact]
    public async Task RowLimitAllowsExactlyTheLimitAndThenStops()
    {
        using var input = new StringReader(Header + "1,1,true,one\n2,2,true,two\n3,3,true,three\n");
        var values = new List<ImportEvent>();
        await foreach (var item in new CsvImporter(CultureInfo.InvariantCulture, maximumRows: 2).ReadAsync(input, TestContext.Current.CancellationToken)) { values.Add(item); }
        Assert.IsType<AcceptedRow>(values[0].Value);
        Assert.IsType<AcceptedRow>(values[1].Value);
        Assert.Equal(new ImportFailure(3, ImportFailureKind.RowLimit), values[2].Value);
        Assert.Equal(3, values.Count);
    }

    [Theory]
    [InlineData("1,2,true,bad\"secret\n")]
    [InlineData("1,2,true,\"unterminated-secret")]
    public async Task MalformedCsvStopsWithSanitizedError(string record)
    {
        var failure = Assert.IsType<ImportFailure>(Assert.Single(await Collect(Header + record)).Value);
        Assert.Equal(ImportFailureKind.MalformedCsv, failure.Kind);
        Assert.DoesNotContain("secret", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FieldSizeLimitStopsBeforeConversion() =>
        Assert.Equal(new ImportFailure(1, ImportFailureKind.FieldLimit), Assert.Single(await Collect(Header + "1,2,true," + new string('x', 4097))).Value);

    [Theory]
    [InlineData(4096, false)]
    [InlineData(4097, false)]
    [InlineData(4096, true)]
    [InlineData(4097, true)]
    public async Task FieldLimitBoundaryIncludesEofAndNewline(int size, bool finalNewline)
    {
        var events = await Collect(Header + "1,2,true," + new string('x', size) + (finalNewline ? "\n" : ""));
        var item = Assert.Single(events);
        if (size == 4096) { Assert.Equal(size, Assert.IsType<AcceptedRow>(item.Value).Value.Note.Length); }
        else { Assert.Equal(new ImportFailure(1, ImportFailureKind.FieldLimit), item.Value); }
    }

    [Fact]
    public async Task CancelledBeforeReadHasExplicitOutcomeAndLeavesCallerReaderOpen()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var input = new StringReader(Header);
        var values = new List<ImportEvent>();
        await foreach (var item in new CsvImporter(CultureInfo.InvariantCulture).ReadAsync(input, source.Token)) { values.Add(item); }
        Assert.Equal(new ImportCancelled(0), Assert.Single(values).Value);
        Assert.Equal('I', input.Read());
    }

    [Fact]
    public async Task CancellationInterruptsInFlightReader()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var input = new BlockingReader();
        await using var iterator = new CsvImporter(CultureInfo.InvariantCulture).ReadAsync(input, source.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var move = iterator.MoveNextAsync().AsTask();
        await input.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        source.Cancel();
        Assert.True(await move.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(new ImportCancelled(0), iterator.Current.Value);
        Assert.False(await iterator.MoveNextAsync());
    }

    [Fact]
    public async Task IoFailureIsAnExplicitSanitizedOutcome()
    {
        using var input = new BrokenReader();
        await using var iterator = new CsvImporter(CultureInfo.InvariantCulture).ReadAsync(input, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(new ImportFailure(0, ImportFailureKind.InputOutput), iterator.Current.Value);
        Assert.False(await iterator.MoveNextAsync());
    }

    [Fact]
    public async Task EarlyDisposalLeavesReaderOwnedByCaller()
    {
        using var input = new OwnedReader(Header + "1,2,true,a\n2,3,true,b\n");
        await using (var iterator = new CsvImporter(CultureInfo.InvariantCulture).ReadAsync(input, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken))
        { Assert.True(await iterator.MoveNextAsync()); }
        Assert.False(input.WasDisposed);
    }

    [Fact]
    public async Task ImporterSnapshotsMutableCultureAtConstruction()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        var importer = new CsvImporter(culture);
        culture.NumberFormat.NumberDecimalSeparator = ":";
        using var input = new StringReader(Header + "1,1.5,true,a\n");
        await using var iterator = importer.ReadAsync(input, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(1.5m, Assert.IsType<AcceptedRow>(iterator.Current.Value).Value.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidProgrammerLimitIsRejected(int limit) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CsvImporter(CultureInfo.InvariantCulture, limit));

    private static async Task<List<ImportEvent>> Collect(string csv)
    {
        using var input = new StringReader(csv);
        var events = new List<ImportEvent>();
        await foreach (var item in new CsvImporter(CultureInfo.InvariantCulture).ReadAsync(input, TestContext.Current.CancellationToken)) { events.Add(item); }
        return events;
    }

    private sealed class BlockingReader : TextReader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
    private sealed class BrokenReader : TextReader
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("secret-input-path"));
    }
    private sealed class OwnedReader(string csv) : StringReader(csv)
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
