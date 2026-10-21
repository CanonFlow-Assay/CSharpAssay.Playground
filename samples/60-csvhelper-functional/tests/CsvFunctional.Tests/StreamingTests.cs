using System.Globalization;
using CsvFunctional.Adapter;
using CsvFunctional.Domain;
using CsvFunctional.Workloads;
using Xunit;

namespace CsvFunctional.Tests;

// Requirements were registered before this workload was implemented. These
// assertions do not inspect or select mutation spans. They are agent-authored,
// not a blind independently adjudicated benchmark.
public sealed class StreamingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4096)]
    public async Task ChunkBoundariesPreserveCompleteOutput(int chunk)
    {
        var specification = new StreamSpec(257, chunk, 0, true, string.Empty);
        var functional = await StreamRunner.FunctionalAsync(specification, TestContext.Current.CancellationToken);
        var direct = await StreamRunner.DirectAsync(specification, TestContext.Current.CancellationToken);
        Assert.Equal(direct, functional);
        Assert.Equal(StreamRunner.Expected(specification), functional);
        Assert.Equal(257L * 258 / 2, functional.IdSum);
        Assert.Equal(257, functional.Accepted);
    }

    [Fact]
    public async Task LargeStreamPreservesClosedFormTotalsWithoutCollectingEvents()
    {
        const int rows = 50_000;
        var result = await StreamRunner.FunctionalAsync(new(rows, 4096, 0, true, string.Empty), TestContext.Current.CancellationToken);
        Assert.Equal(rows, result.Accepted);
        Assert.Equal(0, result.Rejected);
        Assert.Equal((long)rows * (rows + 1) / 2, result.IdSum);
        Assert.Equal(result.IdSum / 100m, result.AmountSum);
        Assert.Equal(rows / 2, result.ActiveCount);
        Assert.Equal(StreamStop.Completed, result.Stop);
    }

    [Fact]
    public async Task MixedStreamReportsEveryBadRecordAndContinues()
    {
        const int rows = 20_000;
        const int interval = 11;
        const int rejected = rows / interval;
        var result = await StreamRunner.FunctionalAsync(new(rows, 127, interval, true, string.Empty), TestContext.Current.CancellationToken);
        Assert.Equal(rows - rejected, result.Accepted);
        Assert.Equal(rejected, result.Rejected);
        Assert.Equal(rejected * 3, result.IssueCount);
        var expectedIds = (long)rows * (rows + 1) / 2 - (long)interval * rejected * (rejected + 1) / 2;
        Assert.Equal(expectedIds, result.IdSum);
        Assert.Equal(expectedIds / 100m, result.AmountSum);
        Assert.Equal(rows / 2 - rejected / 2, result.ActiveCount);
        Assert.Equal(StreamStop.Completed, result.Stop);
    }

    [Fact]
    public async Task FirstEventFromMillionRowsReadsOnlyBoundedPrefixAndLeavesReaderOpen()
    {
        using var input = new GeneratedCsvReader(new(1_000_000, 4096, 0, true, string.Empty));
        await using (var iterator = new CsvImporter(CultureInfo.InvariantCulture, 1_000_000)
            .ReadAsync(input, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal(1, Assert.IsType<AcceptedRow>(iterator.Current.Value).Value.Id);
            Assert.InRange(input.GeneratedRecords, 1, 999);
            Assert.InRange(input.CharactersRead, 1, 8192);
        }
        Assert.False(input.IsDisposed);
    }

    [Fact]
    public async Task CancellationAfterProgressDoesNotEmitAnotherAcceptedRow()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var input = new GeneratedCsvReader(new(50_000, 127, 0, true, string.Empty));
        await using var iterator = new CsvImporter(CultureInfo.InvariantCulture, 50_000)
            .ReadAsync(input, source.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        for (var expected = 1; expected <= 25; expected++)
        {
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal(expected, Assert.IsType<AcceptedRow>(iterator.Current.Value).RecordNumber);
        }
        source.Cancel();
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(new ImportCancelled(26), iterator.Current.Value);
        Assert.False(await iterator.MoveNextAsync());
    }

    [Fact]
    public async Task LargeSourceStopsAtExactConfiguredLimit()
    {
        using var input = new GeneratedCsvReader(new(50_000, 127, 0, true, string.Empty));
        var sink = new DigestSink();
        await foreach (var item in new CsvImporter(CultureInfo.InvariantCulture, 10_000)
            .ReadAsync(input, TestContext.Current.CancellationToken))
        {
            sink.Observe(item);
        }
        var result = sink.Snapshot();
        Assert.Equal(10_000, result.Accepted);
        Assert.Equal(StreamStop.Failed, result.Stop);
        Assert.Equal(10_001, result.StopRecord);
        Assert.InRange(input.GeneratedRecords, 10_001, 10_999);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(127)]
    public async Task GermanAmountsAndMultilineNotesRemainIndependentOfChunkSize(int chunk)
    {
        var specification = new StreamSpec(1007, chunk, 0, true, "de-DE");
        var result = await StreamRunner.FunctionalAsync(specification, TestContext.Current.CancellationToken);
        Assert.Equal(StreamRunner.Expected(specification), result);
        Assert.Equal(1007L * 1008 / 2 / 100m, result.AmountSum);
    }

    [Fact]
    public async Task GeneratedQuotedMultilineRecordHasLiteralExpectedContent()
    {
        using var input = new GeneratedCsvReader(new(7, 1, 0, true, string.Empty));
        await using var iterator = new CsvImporter(CultureInfo.InvariantCulture).ReadAsync(input, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        for (var index = 1; index <= 7; index++) { Assert.True(await iterator.MoveNextAsync()); }
        Assert.Equal(new AcceptedRow(7, new(7, 0.07m, false, "note 7, \"quoted\"\ncontinued")), iterator.Current.Value);
        Assert.False(await iterator.MoveNextAsync());
    }

    [Fact]
    public void ZeroEqualityAndConstantPatternAgreeForEveryShortValue()
    {
        var examined = 0;
        for (var value = (int)short.MinValue; value <= short.MaxValue; value++)
        {
            var numeric = (short)value;
            Assert.Equal(numeric == 0, numeric is 0);
            examined++;
        }
        Assert.Equal(65_536, examined);
    }
}
