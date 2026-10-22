using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CsvFunctional.Workloads;

public static class HeftyCommand
{
    public static async Task<int> ExecuteAsync(string[] arguments)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        if (arguments is ["--hefty-generate", var directory])
        {
            HeftySpec[] specifications =
            [
                new("plain-million", 1_000_000, 16, false, string.Empty, 0),
                new("wide-unicode", 100_000, 1024, true, string.Empty, 0),
                new("german-ledger", 250_000, 64, true, "de-DE", 0),
                new("dirty-ledger", 100_000, 128, true, string.Empty, 100)
            ];
            var fixtures = ImmutableArray.CreateBuilder<HeftyFixture>();
            foreach (var specification in specifications)
            {
                fixtures.Add(await HeftyFiles.GenerateAsync(directory, specification, deadline.Token).ConfigureAwait(false));
            }
            var manifest = new HeftyManifest("csv-hefty-fixtures/0.1", fixtures.ToImmutable());
            await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest), deadline.Token).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(manifest));
            return 0;
        }
        if (arguments is ["--hefty-run", var manifestPath, var caseId, var pipelineName])
        {
            var manifest = JsonSerializer.Deserialize<HeftyManifest>(await File.ReadAllTextAsync(manifestPath, deadline.Token).ConfigureAwait(false));
            var fixture = manifest?.Fixtures.Single(f => f.Specification.Id == caseId);
            if (fixture is null || !Enum.TryParse<HeftyPipeline>(pipelineName, ignoreCase: false, out var pipeline) || !Enum.IsDefined(pipeline)) { return 64; }
            var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? string.Empty, fixture.FileName);
            var samples = new List<object>();
            for (var iteration = -2; iteration < 3; iteration++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
                var allocated = GC.GetTotalAllocatedBytes(precise: true);
                var start = Stopwatch.GetTimestamp();
                var result = await HeftyFiles.RunAsync(path, fixture.Specification, pipeline, deadline.Token).ConfigureAwait(false);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
                var equivalent = result == fixture.Expected;
                var expected = pipeline == HeftyPipeline.Original ? fixture.OriginalExpected : fixture.Expected;
                if (result != expected) { Console.Error.WriteLine("Unexpected output; benchmark invalid."); return 2; }
                samples.Add(new
                {
                    iteration,
                    warmup = iteration < 0,
                    elapsedMilliseconds = elapsed,
                    allocatedBytes,
                    result,
                    equivalentToCompleteExpected = equivalent,
                    gen0 = GC.CollectionCount(0) - collections[0],
                    gen1 = GC.CollectionCount(1) - collections[1],
                    gen2 = GC.CollectionCount(2) - collections[2]
                });
            }
            using var process = Process.GetCurrentProcess();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "csv-hefty-child/0.1",
                executionStatus = "completed",
                caseId,
                pipeline = pipelineName,
                samples,
                runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                os = RuntimeInformation.OSDescription,
                processorCount = Environment.ProcessorCount,
                serverGc = System.Runtime.GCSettings.IsServerGC,
                tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
                tieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "runtime-default",
                buildConfiguration = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "NotObserved",
                peakWorkingSetBytes = process.PeakWorkingSet64,
                scope = "One pipeline/fixture per process; two warm file-read warmups, three measured runs. Includes open/read/parse/conversion/digests/disposal; excludes generation. Peak RSS is process lifetime including warmups/JIT, not managed live heap."
            }));
            return 0;
        }
        Console.Error.WriteLine("Usage: --hefty-generate <directory>; --hefty-run <manifest> <case-id> <Original|ResilientOriginal|Assayed>");
        return 64;
    }
}
