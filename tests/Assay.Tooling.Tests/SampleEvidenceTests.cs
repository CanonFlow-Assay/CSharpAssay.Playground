namespace Assay.Tooling.Tests;

public sealed class SampleEvidenceTests
{
    private static JsonObject Record(string xml, int code = 0, int count = 1) { using var folder = new TemporaryWorkspace(); var path = Path.Combine(folder.Path, "tests.xml"); File.WriteAllText(path, xml); return ApplicationRunner.TestRecord(path, new(code == 0 ? ProcessState.Passed : ProcessState.Failed, code, ""), count, count); }
    private const string Pass = "<assemblies><assembly total=\"1\" passed=\"1\" failed=\"0\"><test name=\"test\" result=\"Pass\"/></assembly></assemblies>";
    [Fact]
    public void ZeroAndSkippedTestsCannotEstablishBaseline()
    {
        Assert.Equal("ToolFailure", Record("<assemblies><assembly total=\"0\" passed=\"0\" failed=\"0\"/></assemblies>").S("state")); Assert.Equal("ToolFailure", Record("<assemblies><assembly total=\"1\" passed=\"0\" skipped=\"1\"><test name=\"test\" result=\"Skip\"/></assembly></assemblies>").S("state"));
    }
    [Fact] public void ProcessFailureCannotReusePass() { Assert.Equal("ToolFailure", Record(Pass, 2).S("state")); Assert.Equal("ToolFailure", Record(Pass, 137).S("state")); }
    [Fact]
    public void MissingSelectedDetailsRejected()
    {
        Assert.Equal("ToolFailure", Record(Pass.Replace("total=\"1\" passed=\"1\"", "total=\"2\" passed=\"2\"", StringComparison.Ordinal), count: 2).S("state"));
    }

    [Fact]
    public void PartialCoverageScopeRejected()
    {
        using var folder = new TemporaryWorkspace(); var path = Path.Combine(folder.Path, "coverage.xml"); File.WriteAllText(path, "<coverage><packages><package name=\"Shape.Domain\"/></packages></coverage>"); _ = Assert.Throws<InvalidDataException>(() => SampleInvestigations.CoverageRecord(path, folder.Path, SampleInvestigations.Shape.Modules));
    }
    [Fact]
    public void ExplicitCoverageRootsBindRelativeAndAbsoluteFiles()
    {
        using var folder = new TemporaryWorkspace(); var source = ApplicationBoundaryTests.Source(folder.Path, "Domain.cs"); var tree = XDocument.Parse($"<coverage><sources><source>{folder.Path}</source></sources></coverage>"); Assert.Equal("src/Domain.cs", SampleInvestigations.CoverageSource(tree, source, folder.Path)); Assert.Equal("src/Domain.cs", SampleInvestigations.CoverageSource(tree, "Domain.cs", folder.Path));
    }
    [Fact]
    public void MissingAmbiguousOutsideCoverageRejected()
    {
        using var folder = new TemporaryWorkspace(); _ = ApplicationBoundaryTests.Source(folder.Path, "src/a/Shared.cs"); _ = ApplicationBoundaryTests.Source(folder.Path, "src/b/Shared.cs"); var outside = ApplicationBoundaryTests.Source(folder.Path, "Outside.cs"); var tree = XDocument.Parse($"<coverage><sources><source>{folder.Path}/src/a</source><source>{folder.Path}/src/b</source></sources></coverage>"); foreach (var file in new[] { "Shared.cs", "Missing.cs", outside })
        {
            _ = Assert.Throws<InvalidDataException>(() => SampleInvestigations.CoverageSource(tree, file, Path.Combine(folder.Path, "src")));
        }
    }
    [Fact]
    public async Task StaleArtifactRemovedBeforeFreshExecution()
    {
        using var folder = new TemporaryWorkspace(); var report = Path.Combine(folder.Path, "tests.xml"); File.WriteAllText(report, Pass); var manifest = Json.Create(("testDll", "missing.dll"), ("minimumTests", 1), ("testReportProtocol", "xunit-mtp-v2")); var result = await ApplicationRunner.Tests(folder.Path, manifest, folder.Path); Assert.Equal("ToolFailure", result.S("state")); Assert.False(File.Exists(report));
    }
    [Fact]
    public void KeyPresenceCannotEnableLiveCalls()
    {
        _ = Assert.Throws<InvalidDataException>(() => AdviceEvaluation.ValidateRequest(false, ["jev"], 8, 0));
    }

    [Fact]
    public void OfflineEvaluationNeedsNoProviderBudget()
    {
        AdviceEvaluation.ValidateRequest(false, [], 8, 0);
    }

    [Fact]
    public void BudgetBeforeLiveProcess()
    {
        _ = Assert.Throws<InvalidDataException>(() => AdviceEvaluation.ValidateRequest(true, ["jev", "openai"], 3, 4));
    }
}
