using System.IO.Compression;
using System.Text;
namespace Assay.Tooling.Tests;

public sealed class MigrationBoundaryTests
{
    private static readonly string[] SourceInputs = ["."];
    [Fact]
    public void ExplicitFalseCannotAuthorizeProviderInference()
    {
        AdviceEvaluation.ValidateRequest(true, ["clef-local", "laya"], 8, 0);
        Assert.Throws<ArgumentException>(() => new Arguments(["--max-requests", "invalid"]).Count("--max-requests", 8));
        var args = new Arguments(["--live=false", "--provider", "openai"]);
        Assert.False(args.Has("--live"));
        Assert.Throws<InvalidDataException>(() => AdviceEvaluation.ValidateRequest(args.Has("--live"), args.All("--provider"), 8, 1));
    }
    [Fact]
    public void DirectoryLinkCannotHideSourceFromSnapshot()
    {
        using var folder = new TemporaryWorkspace();
        var outside = Directory.CreateDirectory(Path.Combine(folder.Path, "outside"));
        File.WriteAllText(Path.Combine(outside.FullName, "Hidden.cs"), "class Hidden {}");
        var app = Directory.CreateDirectory(Path.Combine(folder.Path, "app"));
        Directory.CreateSymbolicLink(Path.Combine(app.FullName, "linked"), outside.FullName);
        Assert.Throws<InvalidDataException>(() => ApplicationRunner.Snapshot(app.FullName, Json.Create(("inputs", SourceInputs))));
    }
    [Fact]
    public async Task CancellationRetainsCancelledState()
    {
        using var folder = new TemporaryWorkspace();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(100);
        var result = await Processes.Run(["dotnet", EnvironmentPaths.Tool, "test-process", "--delay", "10000"], folder.Path, seconds: 30, token: cancellation.Token);
        Assert.Equal(ProcessState.Cancelled, result.State);
    }
    [Fact]
    public void SameCountDifferentNamedTestsCannotCountAsKill()
    {
        using var folder = new TemporaryWorkspace(); var path = Path.Combine(folder.Path, "tests.xml");
        File.WriteAllText(path, "<assemblies><assembly total=\"1\" passed=\"0\" failed=\"1\"><test name=\"Replacement\" result=\"Fail\"/></assembly></assemblies>");
        Assert.Equal("ToolFailure", ApplicationRunner.TestRecord(path, new(ProcessState.Failed, 2, ""), 1, 1, ["Original"]).S("state"));
        File.WriteAllText(path, "<assemblies><assembly total=\"1\" passed=\"1\" failed=\"0\"><test name=\"One\" result=\"Pass\"/></assembly><assembly total=\"1\" passed=\"1\" failed=\"0\"><test name=\"Two\" result=\"Pass\"/></assembly></assemblies>");
        Assert.Equal("ToolFailure", ApplicationRunner.TestRecord(path, new(ProcessState.Passed, 0, ""), 2, 2).S("state"));
    }
    [Fact]
    public void PackageNormalizationPreservesContentsAndIsRepeatable()
    {
        using var folder = new TemporaryWorkspace(); var path = Path.Combine(folder.Path, "CanonFlow.Assay.Workflow.0.1.0-preview.1.nupkg");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry("lib/net11.0/example.dll").Open(); stream.Write(Encoding.UTF8.GetBytes("binary content fixture"));
        }
        PackageTools.Normalize(path); var before = Json.Sha(path); PackageTools.Normalize(path); Assert.Equal(before, Json.Sha(path));
        using var normalized = ZipFile.OpenRead(path); using var content = new StreamReader(normalized.GetEntry("lib/net11.0/example.dll")!.Open()); Assert.Equal("binary content fixture", content.ReadToEnd());
    }
    [Fact]
    public void SignedPackageCannotBeRewritten()
    {
        using var folder = new TemporaryWorkspace(); var path = Path.Combine(folder.Path, "CanonFlow.Assay.Workflow.0.1.0-preview.1.nupkg");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) { using var stream = archive.CreateEntry(".signature.p7s").Open(); stream.WriteByte(1); }
        var before = Json.Sha(path); Assert.Throws<InvalidDataException>(() => PackageTools.Normalize(path)); Assert.Equal(before, Json.Sha(path));
    }
}
