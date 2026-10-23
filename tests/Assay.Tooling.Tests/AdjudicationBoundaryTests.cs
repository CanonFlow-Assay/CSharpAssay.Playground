namespace Assay.Tooling.Tests;

public sealed class AdjudicationBoundaryTests
{
    [Fact]
    public async Task InvalidManifestReturnsStructuredFailureInsteadOfAborting()
    {
        using var app = new TemporaryWorkspace(); var manifest = Fixture(app.Path); var value = Json.Read(manifest); value["minimumTests"] = 0; Json.Write(manifest, value);
        var execution = await Processes.Run(["dotnet", EnvironmentPaths.Tool, "daa", "intake", "--manifest", manifest], app.Path, token: TestContext.Current.CancellationToken);
        Assert.Equal(2, execution.ExitCode);
        Assert.Equal("failed", Json.Parse(execution.Output).S("executionStatus"));
    }

    [Fact]
    public void ExplicitRequirementsCannotBeOmittedFromSnapshot()
    {
        using var app = new TemporaryWorkspace(); var path = Fixture(app.Path); var spec = ApplicationRunner.Load(path);
        var before = Json.Digest(ApplicationRunner.Snapshot(spec.Root, spec.Manifest));
        File.AppendAllText(Path.Combine(app.Path, "requirements.json"), "\n");
        Assert.NotEqual(before, Json.Digest(ApplicationRunner.Snapshot(spec.Root, spec.Manifest)));
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("add")]
    [InlineData("delete")]
    public async Task BinaryChangesRefuseInvestigationBeforeProbeSelection(string change)
    {
        using var app = new TemporaryWorkspace(); var manifest = Fixture(app.Path); var run = Receipt(app.Path, manifest);
        var binary = Path.Combine(app.Path, "bin/worker/Worker.dll");
        switch (change)
        {
            case "replace": File.WriteAllText(binary, "replacement binary"); break;
            case "add": File.WriteAllText(Path.Combine(app.Path, "bin/worker/foreign.dll"), "new dependency"); break;
            case "delete": File.Delete(binary); break;
        }
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ApplicationWorkflow.Investigate(manifest, run, "unregistered", Path.Combine(app.Path, "probe")));
        Assert.Contains("Compiled application", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManifestChangeRefusesInvestigationBeforeProbeSelection()
    {
        using var app = new TemporaryWorkspace(); var manifest = Fixture(app.Path); var run = Receipt(app.Path, manifest);
        var value = Json.Read(manifest); value["minimumTests"] = 2; Json.Write(manifest, value);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ApplicationWorkflow.Investigate(manifest, run, "unregistered", Path.Combine(app.Path, "probe")));
        Assert.Contains("manifest changed", error.Message, StringComparison.Ordinal);
    }

    private static string Fixture(string root)
    {
        ApplicationBoundaryTests.Source(root, "src/Decision.cs");
        foreach (var name in new[] { "Fixture.slnx", "policy.json", "Tests.csproj", "contract.json", "mutations.json", "bin/tests/Tests.dll", "bin/worker/Worker.dll" })
        {
            var path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "fixture");
        }
        Json.Write(Path.Combine(root, "requirements.json"), Json.Parse("""{"schema":"assay-requirements/0.1","requirements":[{"id":"stock","description":"Preserve stock","probeIds":["stock-check"]}]}"""));
        var manifest = Json.Parse("""{"schema":"assay-project/0.2","id":"Fixture","root":".","solution":"Fixture.slnx","policy":"policy.json","requirements":"requirements.json","testProject":"Tests.csproj","testDll":"bin/tests/Tests.dll","workerDll":"bin/worker/Worker.dll","contractBaseline":"contract.json","mutations":"mutations.json","minimumTests":1,"maximumMutations":1,"inputs":["src"],"worker":{"schema":"assay-worker-execution/0.1","scenarioKeys":["stock/application"],"checkIds":["atomic"],"traceVariants":["application"],"traceContract":{"maximumAttempts":2,"deadlineTicks":100,"allowsFallback":false,"atMostOneSideEffect":true}},"probes":[{"id":"stock-check","requirementId":"stock","module":"Behavior","kind":"tests"}]}""");
        var target = Path.Combine(root, "assay.app.json"); Json.Write(target, manifest); return target;
    }

    private static string Receipt(string root, string manifest)
    {
        var spec = ApplicationRunner.Load(manifest);
        var artifacts = Json.Create(("bin/tests/Tests.dll", Json.Sha(Path.Combine(root, "bin/tests/Tests.dll"))), ("bin/worker/Worker.dll", Json.Sha(Path.Combine(root, "bin/worker/Worker.dll"))));
        var path = Path.Combine(root, "evidence/generated/run.json");
        Json.Write(path, Json.Create(("schema", "assay-application-run/0.1"), ("executionStatus", "completed"), ("projectId", "Fixture"), ("snapshotSha256", Json.Digest(ApplicationRunner.Snapshot(root, spec.Manifest))), ("toolkitSha256", ApplicationRunner.ToolkitIdentity()), ("manifestSha256", Json.Digest(spec.Manifest)), ("applicationArtifactsSha256", artifacts), ("evidenceSha256", new JsonObject())));
        return path;
    }
}
