using CsvFunctional.Workloads;
using Xunit;

namespace CsvFunctional.Tests;

public sealed class RoundTripTests
{
    [Theory]
    [InlineData("")]
    [InlineData("de-DE")]
    public async Task PhysicalExportsMatchOriginalBytesAndRegisteredValues(string culture)
    {
        var folder = Directory.CreateTempSubdirectory("csv-export-");
        try
        {
            var fixture = await HeftyFiles.GenerateAsync(folder.FullName, new("roundtrip", 37, 128, true, culture, 0), TestContext.Current.CancellationToken);
            var original = Path.Combine(folder.FullName, "original.csv");
            var assayed = Path.Combine(folder.FullName, "assayed.csv");
            foreach (var (pipeline, output) in new[] { (HeftyPipeline.Original, original), (HeftyPipeline.Assayed, assayed) })
            {
                await RoundTripCommand.RunAsync(Path.Combine(folder.FullName, fixture.FileName), output, fixture.Specification, pipeline, TestContext.Current.CancellationToken);
                Assert.Equal(fixture.Expected, await HeftyFiles.RunAsync(output, fixture.Specification, HeftyPipeline.Original, TestContext.Current.CancellationToken));
            }
            Assert.Equal(await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(assayed, TestContext.Current.CancellationToken));
        }
        finally { folder.Delete(true); }
    }
}
