using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using CsvFunctional.Workloads;

if (args is ["--allocation-profile"]) { return AllocationProfile.Execute(); }
if (args is ["--roundtrip", var roundTripDirectory]) { return await RoundTripCommand.ExecuteAsync(roundTripDirectory); }

if (args.Length > 0 && args[0].StartsWith("--hefty-", StringComparison.Ordinal))
{
    return await HeftyCommand.ExecuteAsync(args);
}

if (args is not ["--benchmark"])
{
    Console.Error.WriteLine("Usage: CsvFunctional.Workloads --benchmark");
    return 64;
}
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
var samples = new List<object>();
foreach (var count in new[] { 10_000, 100_000 })
{
    var specification = new StreamSpec(count, 4096, 0, true, string.Empty);
    var expected = StreamRunner.Expected(specification);
    for (var iteration = -2; iteration < 5; iteration++)
    {
        foreach (var mode in iteration % 2 == 0 ? new[] { "direct", "functional" } : new[] { "functional", "direct" })
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var start = Stopwatch.GetTimestamp();
            var digest = mode == "direct" ? await StreamRunner.DirectAsync(specification, deadline.Token)
                : await StreamRunner.FunctionalAsync(specification, deadline.Token);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var allocationBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
            if (digest != expected)
            {
                Console.Error.WriteLine("Output digest mismatch; timing evidence is invalid.");
                return 2;
            }
            samples.Add(new
            {
                mode,
                iteration,
                warmup = iteration < 0,
                rows = count,
                elapsedMilliseconds = elapsed,
                allocatedBytes = allocationBytes,
                digest
            });
        }
    }
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    schema = "csv-stream-benchmark/0.1",
    executionStatus = "completed",
    samples,
    runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    serverGc = System.Runtime.GCSettings.IsServerGC,
    buildConfiguration = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "NotObserved",
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
    processorCount = Environment.ProcessorCount,
    stopwatchFrequency = Stopwatch.Frequency,
    allocationCounter = "Process-wide managed allocated bytes; includes background allocation and is not retained/peak memory.",
    scope = "Generated valid CSV, synchronous-completing asynchronous reads, mapping/conversion plus identical digest sink; no file/network IO. Direct mapping has fewer failure/limit guarantees."
}));
return 0;
