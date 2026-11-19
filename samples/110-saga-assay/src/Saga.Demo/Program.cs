using System.Text.Json;
using Playground.Saga.Demo;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: Saga.Demo <runtime-traces.json> [fresh-outbox-directory]");
    return 64;
}

var traces = Scenarios.Run();
if (!traces.All(trace => EvidenceJournal.Verify(trace.Entries, trace.HeadSha256)))
{
    Console.Error.WriteLine("Simulated journal integrity check failed.");
    return 1;
}

var report = new { Schema = "saga-assay-simulation/2", Simulated = true, Traces = traces };
WriteReport(args[0], report);
if (args.Length == 2)
{
    var refundTrace = traces.Single(trace => trace.Scenario == "confirmed-nonpayment");
    var recovery = OutboxExercise.Run(refundTrace, args[1]);
    WriteReport(Path.Combine(args[1], "recovery.json"), recovery);
}

foreach (var trace in traces)
{
    Console.WriteLine(trace.Scenario + " -> " + trace.FinalState);
    foreach (var step in trace.Entries)
    {
        var observed = step.Observation;
        Console.WriteLine($"  {step.Sequence,2} {observed.Partner,-16} {observed.Event,-20} {observed.State,-18} {observed.Action,-16} {observed.Evidence}");
    }
}

return 0;

static void WriteReport<T>(string path, T report)
{
    using var destination = new FileStream(path, FileMode.CreateNew);
    JsonSerializer.Serialize(destination, report, new JsonSerializerOptions { WriteIndented = true });
}
