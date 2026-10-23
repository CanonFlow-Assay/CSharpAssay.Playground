namespace Assay.Tooling;

public static partial class Scaffolding
{
    private static readonly string[] values = new[] { "src", "tests", "worker", "App.slnx", ".csassay.json", "requirements.json", "contract.json", "mutations.json", "assay.app.json", "global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.config", ".editorconfig" };
    private static readonly string[] valuesArray = new[] { "src" };

    public static async Task<JsonObject> Create(string destination, string name)
    {
        Json.Check(MyRegex.IsMatch(name), "Choose a simple C# application identity.");
        destination = Path.GetFullPath(destination);
        Json.Check(!Directory.Exists(destination) && !File.Exists(destination), "Scaffolding never overwrites an existing destination.");
        var source = Path.Combine(Root, "samples/70-polly-resilience");
        var parent = Path.GetDirectoryName(destination)!; _ = Directory.CreateDirectory(parent);
        using (var temporary = new TemporaryWorkspace(".assay-new-", parent))
        {
            var staging = temporary.Path;
            foreach (var file in new[] { "global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", ".editorconfig" })
            {
                File.Copy(Path.Combine(Root, file), Path.Combine(staging, file));
            }

            foreach (var project in new[] { "Domain", "Adapter" })
            {
                var target = Path.Combine(staging, "src", name + "." + project); _ = Directory.CreateDirectory(target);
                foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "src/PollyAssay." + project), "*.cs"))
                {
                    File.WriteAllText(Path.Combine(target, Path.GetFileName(file)), File.ReadAllText(file).Replace("PollyAssay", name, StringComparison.Ordinal));
                }
            }
            _ = Directory.CreateDirectory(Path.Combine(staging, "worker"));
            File.WriteAllText(Path.Combine(staging, "worker/Program.cs"), File.ReadAllText(Path.Combine(source, "worker/Program.cs")).Replace("PollyAssay", name, StringComparison.Ordinal));
            var testFolder = Path.Combine(staging, "tests", name + ".Tests"); _ = Directory.CreateDirectory(testFolder);
            File.WriteAllText(Path.Combine(testFolder, "BehaviorTests.cs"), File.ReadAllText(Path.Combine(Root, "tests/Assay.Workflow.Tests/PollyTests.cs")).Replace("PollyAssay", name, StringComparison.Ordinal).Replace("Assay.Workflow.Tests", name + ".Tests", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(testFolder, "WorkflowApiTests.cs"), $$"""
                using Assay.Workflow.Domain;
                using Playground.DecisionAssist.Domain;
                using Xunit;
                namespace {{name}}.Tests;
                public sealed class WorkflowApiTests
                {
                    [Fact]
                    public void UnsupportedApplicationScopeRequestsContextThroughPackagedApi()
                    {
                        var assessment = new Assessment(AssessmentRules.Schema, new string('a', 64), new string('b', 64), SupportStatus.Unsupported, ExecutionState.NotRun,
                            [new("fetch", "Declared requirement", [])], []);
                        Assert.IsType<ContextRequired>(ApplicationAssistance.Decide(assessment, [], 1).Value);
                    }
                }
                """);
            File.Copy(Path.Combine(Root, "tests/Directory.Build.props"), Path.Combine(staging, "tests/Directory.Build.props"));
            var projects = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [$"src/{name}.Domain/{name}.Domain.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"CsAssay.Analyzers\" PrivateAssets=\"all\" /></ItemGroup></Project>",
                [$"src/{name}.Adapter/{name}.Adapter.csproj"] = $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../{name}.Domain/{name}.Domain.csproj\" /><PackageReference Include=\"CanonFlow.Assay.Workflow\" VersionOverride=\"0.1.0-preview.1\" /><PackageReference Include=\"Polly.Core\" VersionOverride=\"8.6.5\" /><PackageReference Include=\"Polly.RateLimiting\" VersionOverride=\"8.6.5\" /><PackageReference Include=\"CsAssay.Analyzers\" PrivateAssets=\"all\" /></ItemGroup></Project>",
                ["worker/Worker.csproj"] = $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><ProjectReference Include=\"../src/{name}.Adapter/{name}.Adapter.csproj\" /></ItemGroup></Project>",
                [$"tests/{name}.Tests/{name}.Tests.csproj"] = $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/{name}.Adapter/{name}.Adapter.csproj\" /><PackageReference Include=\"CanonFlow.DecisionAssist\" VersionOverride=\"0.1.0-preview.1\" /><PackageReference Include=\"coverlet.MTP\" /></ItemGroup></Project>"
            };
            foreach (var (path, text) in projects)
            {
                File.WriteAllText(Path.Combine(staging, path), text + "\n");
            }

            Json.Write(Path.Combine(testFolder, "coverlet.mtp.appsettings.json"), Json.Create(("Coverlet", Json.Create(("Include", $"[{name}.Domain]*,[{name}.Adapter]*"), ("Exclude", ""), ("ExcludeByAttribute", ""), ("Format", "cobertura"), ("UseSourceLink", false), ("IncludeTestAssembly", false), ("SkipAutoProps", false), ("DisableManagedInstrumentationRestore", false)))));
            new XDocument(new XElement("Solution", projects.Keys.Select(p => new XElement("Project", new XAttribute("Path", p))))).Save(Path.Combine(staging, "App.slnx"));
            new XDocument(new XElement("configuration",
                new XElement("config", new XElement("add", new XAttribute("key", "globalPackagesFolder"), new XAttribute("value", ".packages/cache"))),
                new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "assay-local"), new XAttribute("value", Path.Combine(Root, ".packages"))), new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))),
                new XElement("packageSourceMapping", new XElement("packageSource", new XAttribute("key", "assay-local"), new XElement("package", new XAttribute("pattern", "CsAssay.*")), new XElement("package", new XAttribute("pattern", "CanonFlow.*"))), new XElement("packageSource", new XAttribute("key", "nuget.org"), new XElement("package", new XAttribute("pattern", "*")))))).Save(Path.Combine(staging, "NuGet.config"));
            foreach (var filename in new[] { "requirements.json", "contract.json", "upstream.json" })
            {
                File.Copy(Path.Combine(source, filename), Path.Combine(staging, filename));
            }

            CopyTree(Path.Combine(source, "licenses"), Path.Combine(staging, "licenses"));
            var corpus = Json.Read(Path.Combine(source, "mutations.json"));
            foreach (var item in corpus.A("cases"))
            {
                item!["path"] = item.S("path").Replace("samples/70-polly-resilience/", "", StringComparison.Ordinal).Replace("PollyAssay", name, StringComparison.Ordinal);
            }

            Json.Write(Path.Combine(staging, "mutations.json"), corpus);
            var policy = Json.Read(Path.Combine(Root, ".csassay.workflow.json"));
            policy["release"]!["tests"] = Json.Array(new[] { Json.Create(("input", $"tests/{name}.Tests/{name}.Tests.csproj"), ("configuration", "Release"), ("noBuild", true), ("minimumExpectedTests", 14)) });
            policy["boundaries"] = Json.Create(("coreProjects", new[] { $"src/{name}.Domain/{name}.Domain.csproj" }), ("shellProjects", projects.Keys.Where(p => !p.Contains(".Domain/", StringComparison.Ordinal))), ("coreNamespaces", new[] { name + ".Domain" }), ("shellNamespaces", new[] { name + ".Adapter", name + ".Tests" }));
            policy["representations"]!["closedTypes"] = Json.Array(new[] { name + ".Domain.FetchOutcome" });
            Json.Write(Path.Combine(staging, ".csassay.json"), policy);
            var original = Json.Read(Path.Combine(source, "assay.app.json"));
            var manifest = Json.Create(("schema", "assay-project/0.1"), ("id", name), ("root", "."), ("solution", "App.slnx"), ("policy", ".csassay.json"), ("requirements", "requirements.json"), ("testProject", $"tests/{name}.Tests/{name}.Tests.csproj"), ("testDll", $"tests/{name}.Tests/bin/Release/net11.0/{name}.Tests.dll"), ("workerDll", "worker/bin/Release/net11.0/Worker.dll"), ("contractBaseline", "contract.json"), ("mutations", "mutations.json"), ("minimumTests", 14), ("maximumMutations", 8), ("inputs", values), ("worker", original["worker"]), ("probes", original["probes"]), ("productionRoots", valuesArray), ("testReportProtocol", original["testReportProtocol"]), ("coverageProvider", "coverlet-mtp"), ("coverageModules", new[] { name + ".Domain", name + ".Adapter" }));
            Json.Write(Path.Combine(staging, "assay.app.json"), manifest);
            File.WriteAllText(Path.Combine(staging, "AGENTS.md"), "Follow /home/arun/Developer/AGENTS.md when available. Pinned net11 functional document-fetch starter. Preserve native unions, immutable core, effects at boundaries, requirements, named tests and contract pins. Never suppress rules, lower minima, exclude survivors or infer missing evidence. DAA advice cannot grant Pass.\n");
            File.WriteAllText(Path.Combine(staging, ".gitignore"), "**/bin/\n**/obj/\n.packages/\nevidence/generated/\nTestResults/\n");
            Directory.Move(staging, destination);
        }
        var output = Path.Combine(destination, "evidence/generated/bootstrap");
        (await Processes.Run(["dotnet", "restore", Path.Combine(destination, "App.slnx"), "--force-evaluate"], destination, Path.Combine(output, "restore.log"))).Require("Application bootstrap restore");
        (await Processes.Run(["dotnet", "format", "whitespace", Path.Combine(destination, "App.slnx"), "--no-restore"], destination, Path.Combine(output, "format.log"))).Require("Application formatting");
        return Json.Create(("schema", "assay-new-application/0.1"), ("application", destination), ("manifest", Path.Combine(destination, "assay.app.json")), ("template", "Bounded document-fetch starter; not an arbitrary application generator."), ("next", "Run daa verify, then daa plan."));
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,47}$")]
    private static partial Regex MyRegex { get; }
}
