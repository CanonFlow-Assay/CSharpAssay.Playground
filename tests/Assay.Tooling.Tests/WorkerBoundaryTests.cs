namespace Assay.Tooling.Tests;

public sealed class WorkerBoundaryTests
{
    private static JsonNode Requirements()
    {
        return Json.Parse(/*lang=json,strict*/ """{"requirements":[{"id":"stock","description":"Conserve stock.","probeIds":["stock-check"],"criteria":[{"id":"conservation","text":"Stock cannot become negative.","testNames":["Inventory.Conservation"]}]}]}""");
    }

    private static JsonNode Manifest()
    {
        return Json.Parse(/*lang=json,strict*/ """{"schema":"assay-project/0.2","worker":{"schema":"assay-worker-execution/0.1","scenarioKeys":["reserve/application","replay/application"],"checkIds":["atomic"],"traceVariants":["application"],"traceContract":{"maximumAttempts":2,"deadlineTicks":100,"allowsFallback":false,"atMostOneSideEffect":true}},"probes":[{"id":"stock-check","requirementId":"stock","module":"Behavior","kind":"tests"}]}""");
    }

    private static JsonNode Worker()
    {
        return Json.Parse(/*lang=json,strict*/ """{"schema":"assay-worker-execution/0.1","executionStatus":"completed","scenarios":[{"id":"reserve","variant":"application","passed":true,"trace":[{"operation":"reserve"}]},{"id":"replay","variant":"application","passed":true,"trace":[{"operation":"replay"}]}],"infrastructureChecks":[{"id":"atomic","passed":true}],"observedContract":{"members":[],"outcomes":[]}}""");
    }

    private static JsonObject Observe(JsonNode? worker = null, JsonNode? manifest = null, JsonNode? requirements = null)
    {
        return WorkerProtocol.Observe(worker ?? Worker(), WorkerProtocol.Specification(manifest ?? Manifest(), requirements ?? Requirements()).Worker);
    }

    [Fact]
    public void NonFetchApplicationDeclaresOwnCounts()
    {
        Assert.Equal("Passed", Observe().S("state"));
    }

    [Fact] public void DuplicateCannotReplaceMissingScenario() { var w = Worker(); w.A("scenarios")[1] = w.A("scenarios")[0]!.DeepClone(); _ = Assert.Throws<InvalidDataException>(() => Observe(w)); }
    [Fact] public void SameCountsDifferentIdentitiesRejected() { var w = Worker(); w.A("scenarios")[1]!["id"] = "unregistered"; _ = Assert.Throws<InvalidDataException>(() => Observe(w)); }
    [Fact] public void MissingInfrastructureIncomplete() { var w = Worker(); w["infrastructureChecks"] = new JsonArray(); _ = Assert.Throws<InvalidDataException>(() => Observe(w)); }
    [Fact] public void StringTrueCannotPass() { var w = Worker(); w.A("scenarios")[0]!["passed"] = "true"; _ = Assert.Throws<InvalidDataException>(() => Observe(w)); }

    private static readonly string[] expected = new[] { "reserve" };

    [Fact] public void FailedObservationRetained() { var w = Worker(); w.A("scenarios")[0]!["passed"] = false; Assert.Equal(expected, Observe(w).Strings("failedObservations")); }
    [Fact] public void EmptyTraceNotSuccess() { var w = Worker(); foreach (var s in w.A("scenarios")) { s!["trace"] = new JsonArray(); } _ = Assert.Throws<InvalidDataException>(() => Observe(w)); }
    [Fact] public void OtherScenariosCannotHideOneMissingTrace() { var w = Worker(); w.A("scenarios")[0]!["trace"] = new JsonArray(); Assert.Throws<InvalidDataException>(() => Observe(w)); }
    [Fact] public void ArbitraryProbeCommandRejected() { var m = Manifest(); m.A("probes")[0]!["kind"] = "shell"; _ = Assert.Throws<InvalidDataException>(() => Observe(manifest: m)); }
    [Fact] public void UnregisteredRequirementProbeRejected() { var r = Requirements(); r.A("requirements")[0]!.A("probeIds").Add("missing"); _ = Assert.Throws<InvalidDataException>(() => Observe(requirements: r)); }
    [Fact] public void DuplicateProbeRejected() { var m = Manifest(); m.A("probes").Add(m.A("probes")[0]!.DeepClone()); _ = Assert.Throws<InvalidDataException>(() => Observe(manifest: m)); }
    [Fact] public void TestsCannotClaimContractComparison() { var m = Manifest(); m.A("probes")[0]!["module"] = "ContractAssay"; _ = Assert.Throws<InvalidDataException>(() => Observe(manifest: m)); }
    [Fact] public void WorkerCannotClaimNamedTests() { var m = Manifest(); m.A("probes")[0]!["kind"] = "worker"; _ = Assert.Throws<InvalidDataException>(() => Observe(manifest: m)); }
    [Fact]
    public void TraceContractRequiresExactTypes()
    {
        foreach (var (key, value) in new (string, object)[] { ("maximumAttempts", true), ("deadlineTicks", 1.5), ("allowsFallback", "false"), ("atMostOneSideEffect", 1) }) { var m = Manifest(); m["worker"]!["traceContract"]![key] = Json.Node(value); _ = Assert.Throws<InvalidDataException>(() => WorkerProtocol.Specification(m, Requirements())); }
    }

    private static readonly string[] values = new[] { "Inventory.Conservation" };

    [Fact]
    public void NamedTestMapsToRequirement()
    {
        Assert.Equal("Passed", WorkerProtocol.TraceRequirements(Requirements(), Json.Create(("state", "Passed"), ("testNames", values)), Observe(), true).S("state"));
    }

    private static readonly string[] valuesArray = new[] { "Other.Test" };

    [Fact]
    public void WrongNamedTestCannotCoverCriterion()
    {
        Assert.Equal("Failed", WorkerProtocol.TraceRequirements(Requirements(), Json.Create(("state", "Passed"), ("testNames", valuesArray)), Observe(), true).S("state"));
    }

    [Fact] public void MissingCriteriaRemainUnresolved() { var r = Requirements(); r.A("requirements")[0]!["criteria"] = new JsonArray(); Assert.Equal("Failed", WorkerProtocol.TraceRequirements(r, Json.Create(("state", "Passed")), Observe(), true).S("state")); }
    [Fact] public void DuplicateCriteriaRejected() { var r = Requirements(); var c = r.A("requirements")[0]!.A("criteria"); c.Add(c[0]!.DeepClone()); _ = Assert.Throws<InvalidDataException>(() => WorkerProtocol.TraceRequirements(r, Json.Create(("state", "Passed")), Observe(), true)); }
    [Fact]
    public void MutableBudgetCannotRewriteReceiptIdentity()
    {
        using var folder = new TemporaryWorkspace(); File.WriteAllText(Path.Combine(folder.Path, "receipt.json"), /*lang=json,strict*/ "{\"verdict\":\"Fail\"}"); var budget = Path.Combine(folder.Path, "probe-budget.json"); File.WriteAllText(budget, /*lang=json,strict*/ "{\"remaining\":3}"); var before = ApplicationWorkflow.EvidenceIdentity(folder.Path); File.WriteAllText(budget, /*lang=json,strict*/ "{\"remaining\":2}"); Assert.True(JsonNode.DeepEquals(before, ApplicationWorkflow.EvidenceIdentity(folder.Path))); File.WriteAllText(Path.Combine(folder.Path, "receipt.json"), /*lang=json,strict*/ "{\"verdict\":\"Pass\"}"); Assert.False(JsonNode.DeepEquals(before, ApplicationWorkflow.EvidenceIdentity(folder.Path)));
    }
}
