using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assay.Workflow.Domain;
using Playground.DecisionAssist.Domain;

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
};
if (args is not [var command, var input] || command is not ("decide" or "modules")) { Console.Error.WriteLine("Usage: Assay.Workflow.Cli <decide|modules> <packet.json>"); return 64; }
try
{
    if (new FileInfo(input).Length > 2 * 1024 * 1024) { throw new JsonException("Packet exceeds the 2 MiB boundary."); }
    var text = await File.ReadAllTextAsync(input);
    using var document = JsonDocument.Parse(text);
    CheckUnique(document.RootElement);
    if (command == "decide")
    {
        var packet = JsonSerializer.Deserialize<DecisionPacket>(text, options) ?? throw new JsonException();
        var decision = packet.ProposedProbe.Length == 0
            ? ApplicationAssistance.Decide(packet.Assessment, packet.Registry, packet.RemainingProbes)
            : ApplicationAssistance.Select(packet.Assessment, packet.Registry, packet.RemainingProbes, packet.ProposedProbe, packet.ProposalSnapshot);
        var projection = decision switch
        {
            ProbeRequested p => new DecisionProjection("RunRegisteredProbe", p.ProbeId, p.RequirementId, p.Reason),
            ContextRequired p => new DecisionProjection("RequestContext", "", "", p.Reason),
            SurvivorReview p => new DecisionProjection("ReviewSurvivor", "", "", p.Reason),
            BudgetExhausted => new DecisionProjection("BudgetExhausted", "", "", "Registered investigation budget exhausted."),
            NoInvestigation => new DecisionProjection("NoInvestigation", "", "", "No observed issue requires a registered investigation.")
        };
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "assay-application-decision/0.1", snapshotSha256 = packet.Assessment.SnapshotSha256, workflowStatus = AssessmentRules.Evaluate(packet.Assessment).ToString(), decision = projection, authority = "Advisory only; no execution or verification authority." }, options));
    }
    else
    {
        var packet = JsonSerializer.Deserialize<ModulePacket>(text, options) ?? throw new JsonException();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "assay-preview-module-results/0.1",
            contractDifferences = ContractAssay.Compare(packet.BaselineContract, packet.CandidateContract),
            riskPriorities = CodeRiskAssay.Rank(packet.Risk),
            resilienceIssues = ResilienceAssay.Check(packet.Trace, packet.TraceContract),
            authority = "Bounded preview observations; not admitted analyzer rules or automatic release approval."
        }, options));
    }
    return 0;
}
catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
{
    Console.Error.WriteLine("Invalid or incomplete workflow packet: " + failure.GetType().Name);
    return 2;
}

static void CheckUnique(JsonElement value)
{
    if (value.ValueKind == JsonValueKind.Object)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name)) { throw new JsonException("Duplicate packet property."); }
            CheckUnique(property.Value);
        }
    }
    else if (value.ValueKind == JsonValueKind.Array) { foreach (var item in value.EnumerateArray()) { CheckUnique(item); } }
}

internal sealed record DecisionPacket(Assessment Assessment, ImmutableArray<RegisteredProbe> Registry, int RemainingProbes, string ProposedProbe, string ProposalSnapshot);
internal sealed record DecisionProjection(string Action, string ProbeId, string RequirementId, string Reason);
internal sealed record ModulePacket(ContractSnapshot BaselineContract, ContractSnapshot CandidateContract, ImmutableArray<RiskObservation> Risk, ImmutableArray<OperationTrace> Trace, TraceContract TraceContract);
