namespace Assay.Tooling.Tests;

public sealed class PreviewStateBoundaryTests
{
    private static readonly string[] DuplicateOutcomes = ["ok", "ok"];
    private static readonly string[] ModuleNames = ["CSharpAssay", "ContractAssay"];
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CliEvidenceStatesReachTheDaaContextBoundary(bool complete)
    {
        using var app = new TemporaryWorkspace();
        var baseline = Json.Parse("""{"members":[{"id":"status","type":"System.String","required":true}],"outcomes":["ok"]}""");
        var candidate = baseline.DeepClone();
        if (!complete) { candidate["outcomes"] = Json.Array(DuplicateOutcomes); }
        var trace = Json.Parse("""[{"operation":"op","sequence":1,"attempt":1,"elapsedTicks":0,"kind":"Attempt"},{"operation":"op","sequence":2,"attempt":1,"elapsedTicks":1,"kind":"Success"}]""");
        if (!complete) { trace.AsArray().RemoveAt(1); }
        var path = Path.Combine(app.Path, "modules.json");
        Json.Write(path, Json.Create(("baselineContract", baseline), ("candidateContract", candidate),
            ("risk", Json.Parse("""[{"id":"file.cs","changed":false,"coverageKnown":false,"lineCoverage":0,"complexity":0,"survivingMutants":0}]""")),
            ("trace", trace), ("traceContract", Json.Parse("""{"maximumAttempts":3,"deadlineTicks":100,"allowsFallback":false,"atMostOneSideEffect":true}"""))));
        var execution = await Processes.Run(["dotnet", EnvironmentPaths.Decision, "modules", path], app.Path, token: TestContext.Current.CancellationToken);
        Assert.Equal(0, execution.ExitCode);
        var modules = Json.Parse(execution.Output);
        Assert.Equal(complete ? "Passed" : "Invalid", modules.S("contractState"));
        Assert.Equal(complete ? "Passed" : "Invalid", modules.S("resilienceState"));
        Assert.Equal("Passed", modules.S("riskState"));
        var snapshot = new string('a', 64); var requirements = new string('b', 64);
        var assessment = Json.Create(("schema", "assay-application-assessment/0.1"), ("snapshotSha256", snapshot), ("requirementsSha256", requirements), ("support", "Native"), ("baseline", "Passed"),
            ("requirements", Json.Parse("""[{"id":"r","description":"Preserve the contract","probeIds":["contract"]}]""")),
            ("modules", ModuleNames.Select(name => Json.Create(("module", name), ("state", name == "CSharpAssay" ? "Passed" : modules.S("contractState")), ("required", true), ("authoritative", name == "CSharpAssay"), ("snapshotSha256", snapshot), ("requirementsSha256", requirements), ("artifacts", new[] { Json.Create(("path", "receipt.json"), ("sha256", new string('c', 64))) }), ("observations", Array.Empty<string>())))));
        Json.Write(path, Json.Create(("assessment", assessment), ("registry", Json.Parse("""[{"id":"contract","requirementId":"r","module":"ContractAssay"}]""")), ("remainingProbes", 1), ("proposedProbe", ""), ("proposalSnapshot", "")));
        execution = await Processes.Run(["dotnet", EnvironmentPaths.Decision, "decide", path], app.Path, token: TestContext.Current.CancellationToken);
        Assert.Equal(0, execution.ExitCode);
        var decision = Json.Parse(execution.Output);
        Assert.Equal(complete ? "ReadyForReview" : "Incomplete", decision.S("workflowStatus"));
        Assert.Equal(complete ? "NoInvestigation" : "RequestContext", decision["decision"].S("action"));
    }

    [Theory]
    [InlineData("Passed")]
    [InlineData("Failed")]
    [InlineData("Invalid")]
    public void CoordinatorPreservesExplicitModuleState(string state)
    {
        Assert.Equal(state, ApplicationWorkflow.ModuleState(Json.Create(("contractState", state)), "contract"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Pass")]
    [InlineData("Survived")]
    public void OlderOrUnsupportedModuleResultsCannotDefaultToPass(string state)
    {
        Assert.Throws<InvalidDataException>(() => ApplicationWorkflow.ModuleState(Json.Create(("contractState", state)), "contract"));
    }
}
