using System.Text.Json;
using Playground.Saga.Demo;

if (args is not [var outputPath])
{
    Console.Error.WriteLine("Usage: Saga.Demo <runtime-traces.json>");
    return 64;
}

var traces = Scenarios.Run();
if (!traces.All(trace => EvidenceJournal.Verify(trace.Entries, trace.HeadSha256)))
{
    Console.Error.WriteLine("Simulated journal integrity check failed.");
    return 1;
}

var report = new { Schema = "saga-assay-simulation/1", Simulated = true, Traces = traces };
using (var destination = new FileStream(outputPath, FileMode.CreateNew))
{
    JsonSerializer.Serialize(destination, report, new JsonSerializerOptions { WriteIndented = true });
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
