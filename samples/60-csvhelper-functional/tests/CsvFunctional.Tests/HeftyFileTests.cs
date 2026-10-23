using System.Globalization;
using System.Security.Cryptography;
using CsvFunctional.Workloads;
using Xunit;

namespace CsvFunctional.Tests;

public sealed class HeftyFileTests
{
    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(true, "de-DE")]
    public async Task PhysicalFilesHaveEqualCompleteOutputAcrossPipelines(bool unicode, string culture)
    {
        var directory = Directory.CreateTempSubdirectory("csv-hefty-test-");
        try
        {
            var fixture = await HeftyFiles.GenerateAsync(directory.FullName, new("valid", 37, 1024, unicode, culture, 0), TestContext.Current.CancellationToken);
            foreach (var pipeline in Enum.GetValues<HeftyPipeline>())
            {
                var result = await HeftyFiles.RunAsync(Path.Combine(directory.FullName, fixture.FileName), fixture.Specification, pipeline, TestContext.Current.CancellationToken);
                Assert.Equal(fixture.Expected, result);
                Assert.Equal(37L * 38 / 2, result.Totals.IdSum);
                Assert.Equal(37L * 38 / 2 / 100m, result.Totals.AmountSum);
                Assert.Equal(37L * 1024, result.Totals.NoteCharacters);
                Assert.Equal(37, result.Totals.Accepted);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(HeftyPipeline.ResilientOriginal)]
    [InlineData(HeftyPipeline.Assayed)]
    public async Task DirtyFileConsumersContinueWithMatchingErrorAndContentDigests(HeftyPipeline pipeline)
    {
        var directory = Directory.CreateTempSubdirectory("csv-hefty-test-");
        try
        {
            var fixture = await HeftyFiles.GenerateAsync(directory.FullName, new("dirty", 37, 128, true, string.Empty, 7), TestContext.Current.CancellationToken);
            var result = await HeftyFiles.RunAsync(Path.Combine(directory.FullName, fixture.FileName), fixture.Specification, pipeline, TestContext.Current.CancellationToken);
            Assert.Equal(fixture.Expected, result);
            Assert.Equal(32, result.Totals.Accepted);
            Assert.Equal(5, result.Totals.Rejected);
            Assert.Equal(15, result.Totals.IssueCount);
            Assert.Equal(StreamStop.Completed, result.Totals.Stop);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task DefaultOriginalFailureHasAnExactVerifiedPrefixRatherThanCompleteEquivalence()
    {
        var directory = Directory.CreateTempSubdirectory("csv-hefty-test-");
        try
        {
            var fixture = await HeftyFiles.GenerateAsync(directory.FullName, new("dirty", 37, 128, true, string.Empty, 7), TestContext.Current.CancellationToken);
            var result = await HeftyFiles.RunAsync(Path.Combine(directory.FullName, fixture.FileName), fixture.Specification, HeftyPipeline.Original, TestContext.Current.CancellationToken);
            Assert.Equal(fixture.OriginalExpected, result);
            Assert.NotEqual(fixture.Expected.OutputSha256, result.OutputSha256);
            Assert.Equal(6, result.Totals.Accepted);
            Assert.Equal(7, result.Totals.StopRecord);
            Assert.Equal(HeftyFailure.TypeConversion, result.Failure);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task UnicodeFixtureHasRealUtf8BytesStableLengthAndCorrectInputHash()
    {
        var directory = Directory.CreateTempSubdirectory("csv-hefty-test-");
        try
        {
            var fixture = await HeftyFiles.GenerateAsync(directory.FullName, new("utf8", 3, 1024, true, "de-DE", 0), TestContext.Current.CancellationToken);
            var path = Path.Combine(directory.FullName, fixture.FileName);
            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.True(bytes.Length > text.Length);
            Assert.Equal(bytes.Length, fixture.FileBytes);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), fixture.FileSha256);
            Assert.StartsWith("id=1|Ω,客户 \"quoted\"\n", HeftyFiles.Note(1, 1024, true), StringComparison.Ordinal);
            Assert.Equal(1024, HeftyFiles.Note(1, 1024, true).Length);
            Assert.Contains("\"0,01\"", text, StringComparison.Ordinal);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task OversizedFieldFixtureIsRejectedBeforeFileCreation()
    {
        var directory = Directory.CreateTempSubdirectory("csv-hefty-test-");
        try
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => HeftyFiles.GenerateAsync(directory.FullName,
                new("oversized", 1, 4097, true, CultureInfo.InvariantCulture.Name, 0), TestContext.Current.CancellationToken));
            Assert.Empty(directory.GetFiles());
        }
        finally { directory.Delete(recursive: true); }
    }
}
