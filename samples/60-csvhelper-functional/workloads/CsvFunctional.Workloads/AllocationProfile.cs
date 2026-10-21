using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using CsvFunctional.Domain;

namespace CsvFunctional.Workloads;

public static class AllocationProfile
{
    public static int Execute()
    {
        const int count = 100_000;
        var culture = CultureInfo.InvariantCulture;
        ImmutableArray<string> fields = ["123", "123.45", "true", "note"];
        var whole = new PresentField(fields[0]);
        var amount = new PresentField(fields[1]);
        var flag = new PresentField(fields[2]);
        var probes = new (string Name, Action Body)[]
        {
            ("field-inputs", () => { Keep(new PresentField(fields[0])); Keep(new PresentField(fields[1])); Keep(new PresentField(fields[2])); }),
            ("conversion-outcomes", () => { Keep(FieldConversions.ParseWhole(whole, culture)); Keep(FieldConversions.ParseAmount(amount, culture)); Keep(FieldConversions.ParseFlag(flag, culture, BooleanLiterals.Default)); }),
            ("empty-issue-builder", () => Keep(ImmutableArray.CreateBuilder<CsvIssue>())),
            ("valid-row-decision", () => Keep(RowDecisions.Convert(1, fields, culture)))
        };
        var results = new List<object>();
        foreach (var (name, body) in probes)
        {
            for (var index = 0; index < 10_000; index++) { body(); }
            for (var iteration = 0; iteration < 3; iteration++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var index = 0; index < count; index++) { body(); }
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                results.Add(new { stage = name, iteration, operations = count, allocatedBytes = bytes, bytesPerOperation = (double)bytes / count });
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "csv-allocation-profile/0.1",
            runtime = RuntimeInformation.FrameworkDescription,
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
            samples = results,
            scope = "Synchronous stage probes with no-inline sinks; union values are consumed without sink boxing. Stages overlap and must not be added. No stack sampling, retained-heap or async-read allocation attribution."
        }));
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Keep<T>(T value) { _ = value; }
}
