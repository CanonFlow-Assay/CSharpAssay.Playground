using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assay.Workflow.Domain;
using PollyAssay.Adapter;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var results = ImmutableArray.CreateBuilder<ScenarioResult>();
foreach (var scenario in Scenarios.Registered)
{
    foreach (var functional in new[] { false, true })
    {
        var result = await Scenarios.RunAsync(scenario, functional, deadline.Token);
        if (result.Response.Status != scenario.ExpectedStatus || result.Attempts != scenario.ExpectedAttempts) { throw new InvalidDataException("Scenario differs from preregistered status/attempt count: " + scenario.Id); }
        var issues = ResilienceAssay.Check(result.Trace, new(3, TimeSpan.FromSeconds(2).Ticks, false, true));
        if (!issues.IsEmpty) { throw new InvalidDataException(string.Join(";", issues)); }
        results.Add(result);
    }
}
foreach (var group in results.GroupBy(r => r.Id))
{
    if (group.Select(r => r.Response).Distinct().Count() != 1 || group.Select(r => r.Attempts).Distinct().Count() != 1) { throw new InvalidDataException("Original and assayed outcomes differ."); }
}
var infrastructure = await Scenarios.InfrastructureChecksAsync(deadline.Token);
Console.WriteLine(JsonSerializer.Serialize(new
{
    schema = "polly-consumer-execution/0.1",
    executionStatus = "completed",
    scenarios = results,
    infrastructureChecks = infrastructure,
    observedContract = Scenarios.ObserveContract(),
    scope = "Pinned Polly consumer settings/delegates and controlled faults/time; not a port or qualification of all Polly internals."
}, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } }));
