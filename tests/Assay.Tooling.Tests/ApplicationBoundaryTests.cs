namespace Assay.Tooling.Tests;

public sealed class ApplicationBoundaryTests
{
    private static JsonObject Record(string xml, int code = 0, int? expected = null)
    {
        using var folder = new TemporaryWorkspace(); var path = Path.Combine(folder.Path, "tests.xml"); File.WriteAllText(path, xml);
        return ApplicationRunner.TestRecord(path, new(code == 0 ? ProcessState.Passed : ProcessState.Failed, code, ""), 1, expected);
    }
    [Fact]
    public async Task TimeoutIsNotPassOrKilledMutation()
    {
        using var folder = new TemporaryWorkspace();
        var result = await Processes.Run(["dotnet", EnvironmentPaths.Tool, "test-process", "--delay", "10000"], folder.Path, Path.Combine(folder.Path, "log"), .1, token: TestContext.Current.CancellationToken);
        Assert.Equal(ProcessState.TimedOut, result.State);
    }
    [Fact]
    public void AbsentReportIsToolFailure()
    {
        Assert.Equal("ToolFailure", ApplicationRunner.TestRecord("/nonexistent/report.xml", new(ProcessState.Passed, 0, ""), 1).S("state"));
    }

    [Fact]
    public void UnknownProtocolCannotSelectACommand()
    {
        _ = Assert.Throws<InvalidDataException>(() => ApplicationRunner.ReportArguments(Json.Create(("testReportProtocol", "arbitrary"))));
    }

    [Fact]
    public void EmptyCoverageCannotClaimMeasurement()
    {
        using var folder = new TemporaryWorkspace(); File.WriteAllText(Path.Combine(folder.Path, "coverage.cobertura.1.xml"), "<coverage><packages/></coverage>"); _ = Assert.Throws<InvalidDataException>(() => ApplicationRunner.NormalizeCoverage(folder.Path, ["Domain"])); Assert.False(File.Exists(Path.Combine(folder.Path, "coverage.cobertura.xml")));
    }
    [Fact]
    public void SameCountWrongModulesCannotReplaceProduction()
    {
        using var folder = new TemporaryWorkspace(); File.WriteAllText(Path.Combine(folder.Path, "coverage.cobertura.1.xml"), "<coverage lines-valid=\"5\"><packages><package name=\"Domain\"/><package name=\"Tests\"/></packages></coverage>"); _ = Assert.Throws<InvalidDataException>(() => ApplicationRunner.NormalizeCoverage(folder.Path, ["Domain", "Adapter"]));
    }
    [Fact]
    public void ZeroTestsCannotPass()
    {
        Assert.Equal("ToolFailure", Record("<assemblies><assembly total=\"0\" passed=\"0\" failed=\"0\"/></assemblies>").S("state"));
    }

    [Fact]
    public void SkippedTestsCannotPass()
    {
        Assert.Equal("ToolFailure", Record("<assemblies><assembly total=\"1\" passed=\"0\" failed=\"0\" skipped=\"1\"><test name=\"fixture\" result=\"Skip\"/></assembly></assemblies>").S("state"));
    }

    [Fact]
    public void SummaryCannotHideMissingExecutions()
    {
        Assert.Equal("ToolFailure", Record("<assemblies><assembly total=\"2\" passed=\"2\" failed=\"0\"><test name=\"fixture\" result=\"Pass\"/></assembly></assemblies>").S("state"));
    }

    [Fact]
    public void FailureWithSuccessfulExitIsToolFailure()
    {
        Assert.Equal("ToolFailure", Record("<assemblies><assembly total=\"1\" passed=\"0\" failed=\"1\"><test name=\"fixture\" result=\"Fail\"/></assembly></assemblies>").S("state"));
    }

    [Fact]
    public void PassingSummaryCannotHideIndividualFailure()
    {
        Assert.Equal("ToolFailure", Record("<assemblies><assembly total=\"1\" passed=\"1\" failed=\"0\"><test name=\"fixture\" result=\"Fail\"/></assembly></assemblies>").S("state"));
    }

    [Fact]
    public void MutantTimeoutRetainsItsState()
    {
        Assert.Equal("TimedOut", ApplicationRunner.TestRecord("ignored", new(ProcessState.TimedOut, 137, ""), 1).S("state"));
    }

    [Fact]
    public void CompletedFailureAndSuccessDiffer()
    {
        Assert.Equal("Passed", Record("<assemblies><assembly total=\"1\" passed=\"1\" failed=\"0\"><test name=\"fixture\" result=\"Pass\"/></assembly></assemblies>").S("state"));
        Assert.Equal("Failed", Record("<assemblies><assembly total=\"1\" passed=\"0\" failed=\"1\"><test name=\"fixture\" result=\"Fail\"/></assembly></assemblies>", 2).S("state"));
    }
    [Fact]
    public void ChangedSelectionIsNotAKill()
    {
        Assert.Equal("ToolFailure", Record("<assemblies><assembly total=\"1\" passed=\"0\" failed=\"1\"><test name=\"fixture\" result=\"Fail\"/></assembly></assemblies>", 2, 2).S("state"));
    }

    [Fact] public void WorkspaceEscapeRejected() { using var folder = new TemporaryWorkspace(); _ = Assert.Throws<InvalidDataException>(() => EnvironmentPaths.Inside(folder.Path, "../outside.csproj")); }
    [Fact]
    public void DuplicateJsonKeysRejected()
    {
        _ = Assert.Throws<InvalidDataException>(() => Json.Parse(/*lang=json,strict*/ "{\"state\":\"Failed\",\"state\":\"Passed\"}"));
    }

    private static readonly string[] values = new[] { "code.cs" };

    [Fact]
    public void SnapshotChangesWithSource()
    {
        using var folder = new TemporaryWorkspace(); var source = Path.Combine(folder.Path, "code.cs"); File.WriteAllText(source, "class Before {}"); var manifest = Json.Create(("inputs", values)); var before = Json.Digest(ApplicationRunner.Snapshot(folder.Path, manifest)); File.WriteAllText(source, "class After {}"); Assert.NotEqual(before, Json.Digest(ApplicationRunner.Snapshot(folder.Path, manifest)));
    }
    [Fact]
    public void NestedCoverageBindsExactSource()
    {
        using var folder = new TemporaryWorkspace(); var name = "samples/app/src/Domain/Decision.cs"; var source = Source(folder.Path, name); var unknown = Source(folder.Path, "src/Unknown.cs"); var report = Path.Combine(folder.Path, "coverage.xml"); File.WriteAllText(report, $"<coverage><class filename=\"{source}\"><lines><line number=\"1\" hits=\"1\"/><line number=\"2\" hits=\"0\"/></lines></class></coverage>"); var rows = ApplicationWorkflow.CoverageObservations(folder.Path, Json.Create((name, "a"), ("src/Unknown.cs", "b")), report, [], []);
        Assert.True(rows.Single(r => r.S("id") == name).B("coverageKnown")); Assert.Equal(50m, Benchmarks.Number(rows.Single(r => r.S("id") == name)!["lineCoverage"])); Assert.False(rows.Single(r => r.S("id") == "src/Unknown.cs").B("coverageKnown")); Assert.True(File.Exists(unknown));
    }
    [Fact]
    public async Task ScaffoldNeverOverwrites()
    {
        using var folder = new TemporaryWorkspace(); var keep = Path.Combine(folder.Path, "keep.txt"); File.WriteAllText(keep, "user work"); _ = await Assert.ThrowsAsync<InvalidDataException>(() => Scaffolding.Create(folder.Path, "Example")); Assert.Equal("user work", File.ReadAllText(keep));
    }
    [Fact]
    public void RelativeCoverageUsesExplicitSourceRoot()
    {
        using var folder = new TemporaryWorkspace(); var name = "samples/app/src/Domain/Decision.cs"; var source = Source(folder.Path, name); var report = Path.Combine(folder.Path, "coverage.xml"); File.WriteAllText(report, $"<coverage><sources><source>{Path.GetDirectoryName(Path.GetDirectoryName(source))}</source></sources><class filename=\"Domain/Decision.cs\"><lines><line number=\"1\" hits=\"1\"/><line number=\"2\" hits=\"0\"/></lines></class></coverage>"); var row = ApplicationWorkflow.CoverageObservations(folder.Path, Json.Create((name, "a")), report, [], [])[0]; Assert.True(row.B("coverageKnown")); Assert.Equal(50m, Benchmarks.Number(row!["lineCoverage"]));
    }
    [Fact]
    public void AmbiguousCoverageSourcesStayUnknown()
    {
        using var folder = new TemporaryWorkspace(); _ = Source(folder.Path, "a/Shared.cs"); _ = Source(folder.Path, "b/Shared.cs"); var report = Path.Combine(folder.Path, "coverage.xml"); File.WriteAllText(report, $"<coverage><sources><source>{folder.Path}/a</source><source>{folder.Path}/b</source></sources><class filename=\"Shared.cs\"><lines><line number=\"1\" hits=\"1\"/></lines></class></coverage>"); Assert.All(ApplicationWorkflow.CoverageObservations(folder.Path, Json.Create(("a/Shared.cs", "a"), ("b/Shared.cs", "b")), report, [], []), r => Assert.False(r.B("coverageKnown")));
    }
    [Fact]
    public void ForeignCoverageCannotBindMatchingName()
    {
        using var folder = new TemporaryWorkspace(); _ = Source(folder.Path, "app/src/Decision.cs"); _ = Source(folder.Path, "foreign/src/Decision.cs"); var root = Path.Combine(folder.Path, "app"); var report = Path.Combine(root, "coverage.xml"); File.WriteAllText(report, $"<coverage><sources><source>{folder.Path}/foreign</source></sources><class filename=\"src/Decision.cs\"><lines><line number=\"1\" hits=\"1\"/></lines></class></coverage>"); Assert.False(ApplicationWorkflow.CoverageObservations(root, Json.Create(("src/Decision.cs", "a")), report, [], [])[0].B("coverageKnown"));
    }
    [Fact]
    public async Task ScaffoldRejectsCodeInIdentity()
    {
        _ = await Assert.ThrowsAsync<InvalidDataException>(() => Scaffolding.Create("/tmp/unused-assay-test", "App; shell"));
    }

    internal static string Source(string root, string name) { var path = Path.Combine(root, name); _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "class Decision {}"); return path; }
}
