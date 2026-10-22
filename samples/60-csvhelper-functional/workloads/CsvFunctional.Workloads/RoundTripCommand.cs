using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CsvFunctional.Adapter;
using CsvFunctional.Domain;
using CsvHelper;
using CsvHelper.Configuration;

namespace CsvFunctional.Workloads;

public static class RoundTripCommand
{
    public static async Task<int> ExecuteAsync(string directory)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var fixtures = new List<HeftyFixture>();
        foreach (var name in new[] { "", "de-DE" })
        {
            fixtures.Add(await HeftyFiles.GenerateAsync(directory, new(name.Length == 0 ? "roundtrip-invariant" : "roundtrip-german", 50_000, 128, true, name, 0), deadline.Token).ConfigureAwait(false));
        }
        var samples = new List<object>();
        foreach (var fixture in fixtures)
        {
            var expectedBytes = string.Empty;
            for (var iteration = -1; iteration < 3; iteration++)
            {
                foreach (var pipeline in iteration % 2 == 0 ? new[] { HeftyPipeline.Original, HeftyPipeline.Assayed } : new[] { HeftyPipeline.Assayed, HeftyPipeline.Original })
                {
                    var output = Path.Combine(directory, fixture.Specification.Id + "-" + pipeline + ".output.csv");
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    var allocated = GC.GetTotalAllocatedBytes(true);
                    var start = Stopwatch.GetTimestamp();
                    await RunAsync(Path.Combine(directory, fixture.FileName), output, fixture.Specification, pipeline, deadline.Token).ConfigureAwait(false);
                    var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated;
                    // Independent original mapping verifies exported values; byte
                    // hashes also require identical formatting and escaping.
                    var digest = await HeftyFiles.RunAsync(output, fixture.Specification, HeftyPipeline.Original, deadline.Token).ConfigureAwait(false);
                    if (digest != fixture.Expected) { throw new InvalidDataException("Round-trip content differs from registered values."); }
                    await using var file = File.OpenRead(output);
                    var checksum = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file, deadline.Token).ConfigureAwait(false));
                    if (expectedBytes.Length == 0) { expectedBytes = checksum; }
                    if (checksum != expectedBytes) { throw new InvalidDataException("Original and functional output bytes differ."); }
                    samples.Add(new { caseId = fixture.Specification.Id, pipeline = pipeline.ToString(), iteration, warmup = iteration < 0, elapsedMilliseconds = elapsed, allocatedBytes, outputBytes = file.Length, outputSha256 = checksum, digest });
                }
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "csv-roundtrip-workload/0.1",
            executionStatus = "completed",
            fixtures,
            samples,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
            tieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "runtime-default",
            scope = "Counterbalanced pipelines in one worker; one warmup and three measured passes each. Includes physical input read, import/conversion, export/escaping and output flush/disposal. Generation, output hashing and independent original re-import validation excluded. Warm cache, no fsync or atomic-durability guarantee."
        }));
        return 0;
    }

    public static async Task RunAsync(string inputPath, string outputPath, HeftySpec specification, HeftyPipeline pipeline, CancellationToken cancellationToken)
    {
        using var input = new StreamReader(inputPath, new UTF8Encoding(false, true), true, 65536);
        await using var output = new StreamWriter(outputPath, false, new UTF8Encoding(false, true), 65536);
        var culture = CultureInfo.GetCultureInfo(specification.Culture);
        if (pipeline == HeftyPipeline.Original)
        {
            using var reader = new CsvReader(input, new CsvConfiguration(culture) { Delimiter = ",", IgnoreBlankLines = false, MaxFieldSize = 4096, ExceptionMessagesContainRawData = false });
            await using var writer = new CsvWriter(output, new CsvConfiguration(culture) { Delimiter = ",", NewLine = "\r\n", ShouldQuote = _ => true }, leaveOpen: true);
            await writer.WriteRecordsAsync(reader.GetRecordsAsync<DirectRecord>(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        else if (pipeline == HeftyPipeline.Assayed)
        {
            var rows = Accepted(new CsvImporter(culture, specification.Rows).ReadAsync(input, cancellationToken));
            var count = 0;
            await foreach (var item in new CsvExporter(culture, specification.Rows).WriteAsync(rows, output, cancellationToken).ConfigureAwait(false))
            {
                if (item is not ExportedRow) { throw new InvalidDataException("Unexpected export terminal outcome on a valid fixture."); }
                count++;
            }
            if (count != specification.Rows) { throw new InvalidDataException("Incomplete round-trip export."); }
        }
        else { throw new ArgumentOutOfRangeException(nameof(pipeline)); }
    }

    private static async IAsyncEnumerable<ImportedRecord> Accepted(IAsyncEnumerable<ImportEvent> source)
    {
        await foreach (var item in source.ConfigureAwait(false))
        {
            if (item is AcceptedRow row) { yield return row.Value; }
            else { throw new InvalidDataException("Unexpected import outcome on a valid fixture."); }
        }
    }
}
