namespace Assay.Tooling;

public sealed record SampleSettings(string RelativePath, string Solution, string TestDll, string BaselineClass, int BeforeTests, string[] Modules, string Output, bool Csv);
public static partial class SampleInvestigations
{
    public static readonly string[] BuildFiles = ["global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", ".editorconfig"];
    public static readonly SampleSettings Csv = new("samples/60-csvhelper-functional", "CsvFunctional.slnx", "tests/CsvFunctional.Tests/bin/Release/net11.0/CsvFunctional.Tests.dll", "CsvFunctional.Tests.SmokeTests", 2, ["CsvFunctional.Domain", "CsvFunctional.Adapter"], "evidence/generated/csvhelper/functional", true);
    public static readonly SampleSettings Shape = new("samples/40-functional-shape-v0.1", "Shape.slnx", "tests/Shape.Behavior.Tests/bin/Release/net11.0/Shape.Behavior.Tests.dll", "Shape.Behavior.Tests.OrderBehaviorTests", 16, ["Shape.Domain", "Shape.Application", "Shape.Infrastructure", "Shape.Api"], "evidence/generated/shape-orders", false);
    public static JsonObject Bindings(SampleSettings settings)
    {
        var sample = Path.Combine(Root, settings.RelativePath);
        var paths = Files(sample).Where(p => Path.GetExtension(p) is ".cs" or ".csproj" or ".slnx" or ".json" or ".config" or ".csv" or ".txt").Concat(BuildFiles.Select(f => Path.Combine(Root, f))).Concat(Files(Path.Combine(Root, "eng/Assay.Tooling")).Where(p => Path.GetExtension(p) is ".cs" or ".csproj")).Concat(Files(Path.Combine(Root, "samples/50-decision-assistance/src")).Where(p => Path.GetExtension(p) == ".cs")).Concat(Directory.EnumerateFiles(Path.GetDirectoryName(Advice)!).Where(p => Path.GetExtension(p) is ".dll" or ".json")).Append(Path.Combine(Root, "NuGet.config")).Append(Tool).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var result = new JsonObject(); foreach (var path in paths)
        {
            result[Relative(Root, path)] = Json.Sha(path);
        }

        return result;
    }
    public static JsonObject Artifacts(string sample)
    {
        var result = new JsonObject();
        foreach (var path in Directory.EnumerateFiles(sample, "*", SearchOption.AllDirectories).Where(p => p.Contains("/bin/Release/net11.0/", StringComparison.Ordinal) && Path.GetFileName(p).StartsWith("CsvFunctional.", StringComparison.Ordinal) && Path.GetExtension(p) is ".dll" or ".pdb" or ".json").Order(StringComparer.Ordinal))
        {
            result[Relative(sample, path)] = Json.Sha(path);
        }

        return result;
    }
    public static void AssertBindings(JsonNode bindings)
    {
        foreach (var (name, expected) in bindings.AsObject())
        {
            Json.Check(File.Exists(Inside(Root, name)) && Json.Sha(Inside(Root, name)) == expected!.GetValue<string>(), "Bound input changed: " + name);
        }
    }
    public static string ExactEdit(string text, JsonNode item)
    {
        var before = item.S("before"); Json.Check(before.Length > 0 && text.Split(before, StringSplitOptions.None).Length == 2, "Mutation must identify exactly one reviewed span: " + item.S("id"));
        return text.Replace(before, item.S("after"), StringComparison.Ordinal);
    }
    public static string CoverageSource(XDocument tree, string filename, string productionRoot)
    {
        filename = filename.Replace('\\', '/'); productionRoot = Path.GetFullPath(productionRoot);
        var candidates = Path.IsPathRooted(filename) ? new[] { Path.GetFullPath(filename) } : tree.Root!.Element("sources")?.Elements("source").Select(s => s.Value).Where(Path.IsPathRooted).Select(s => Path.GetFullPath(Path.Combine(s, filename))).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var existing = candidates.Where(File.Exists).ToArray();
        Json.Check(existing.Length == 1, "Coverage filename must resolve to exactly one existing source.");
        var source = Inside(productionRoot, Relative(productionRoot, existing[0]));
        return "src/" + Relative(productionRoot, source);
    }
    public static JsonObject CoverageRecord(string path, string productionRoot, string[] expectedModules)
    {
        var tree = XDocument.Load(path); var root = tree.Root!; var modules = root.Element("packages")!.Elements("package").ToArray();
        var names = modules.Select(p => (string)p.Attribute("name")!).ToArray();
        Json.Check(names.Length == names.Distinct(StringComparer.Ordinal).Count() && names.ToHashSet(StringComparer.Ordinal).SetEquals(expectedModules), "Coverage production assembly scope differs.");
        var result = new JsonObject(); foreach (var key in new[] { "lines-covered", "lines-valid", "branches-covered", "branches-valid" })
        {
            result[key] = (int?)root.Attribute(key) ?? -1;
        }

        Json.Check(result.I("lines-valid") > 0 && result.I("branches-valid") > 0 && result.I("lines-covered") >= 0 && result.I("lines-covered") <= result.I("lines-valid") && result.I("branches-covered") >= 0 && result.I("branches-covered") <= result.I("branches-valid"), "Invalid or empty coverage.");
        var hits = new JsonObject();
        foreach (var cls in modules.SelectMany(p => p.Descendants("class")))
        {
            var relative = CoverageSource(tree, (string)cls.Attribute("filename")!, productionRoot);
            foreach (var line in cls.Element("lines")?.Elements("line") ?? []) { var key = relative + ":" + (string?)line.Attribute("number"); hits[key] = Math.Max(hits.I(key), (int?)line.Attribute("hits") ?? 0); }
        }
        result["linePercent"] = Math.Round(100m * result.I("lines-covered") / result.I("lines-valid"), 2); result["branchPercent"] = Math.Round(100m * result.I("branches-covered") / result.I("branches-valid"), 2); result["reportSha256"] = Json.Sha(path); result["lineHits"] = hits;
        var assemblies = new JsonObject(); foreach (var module in modules)
        {
            assemblies[(string)module.Attribute("name")!] = Json.Create(("linePercent", Math.Round(decimal.Parse((string)module.Attribute("line-rate")!, CultureInfo.InvariantCulture) * 100, 2)), ("branchPercent", Math.Round(decimal.Parse((string)module.Attribute("branch-rate")!, CultureInfo.InvariantCulture) * 100, 2)));
        }

        result["assemblies"] = assemblies;
        return result;
    }
    public static async Task<JsonNode> AdviceFor(string packet, params string[] options)
    {
        var execution = await Processes.Run(["dotnet", Advice, packet, .. options], Root, Path.ChangeExtension(packet, ".advice.log"), 40); execution.Require("Decision Assist");
        var value = Json.Parse(execution.Output);
        Json.Check(value.S("schema") == "playground-decision-advice/0.1" && value.S("verificationVerdict") == "Inconclusive", "Advice exceeded its authority boundary.");
        if (options.Length == 0)
        {
            Json.Check(value.S("provider") == "deterministic-baseline", "Unrequested provider call.");
        }

        Json.Write(Path.ChangeExtension(packet, ".advice.json"), value); return value;
    }
    private static async Task<JsonObject> Tests(SampleSettings settings, string sample, string output, int count, bool before, bool coverage = false)
    {
        var manifest = Json.Create(("testDll", settings.TestDll), ("minimumTests", count), ("testReportProtocol", "xunit-mtp-v2"), ("coverageProvider", "coverlet-mtp"), ("coverageModules", settings.Modules));
        var selectedClass = before ? settings.Csv ? settings.BaselineClass : Json.Read(Path.Combine(sample, "mutation-cases.json")).S("baselineClass") : null;
        var result = await ApplicationRunner.Tests(sample, manifest, output, count, coverage, selectedClass: selectedClass);
        Json.Check(result.S("state") is "Passed" or "Failed", "Incomplete sample test execution: " + result.ToJsonString());
        return result;
    }
    public static async Task<JsonObject> Run(SampleSettings settings)
    {
        var sample = Path.Combine(Root, settings.RelativePath); var output = Path.Combine(Root, settings.Output);
        var report = Json.Create(("schema", settings.Csv ? "csv-functional-investigations/0.1" : "shape-order-investigations/0.1"), ("executionStatus", "running"), ("synthetic", true), ("executed", true), ("providerRequests", 0), ("qualityGateApplied", false), ("reviewStatus", "Agent review; human adjudication pending."), ("results", new JsonArray()), ("authority", "Executed source-bound sample experiment; separate from authoritative verification and provider qualification."));
        var reportPath = Path.Combine(output, "investigations.json"); Json.Write(reportPath, report);
        try
        {
            var corpus = Json.Read(Path.Combine(sample, "mutation-cases.json")); var cases = corpus.A("cases");
            Json.Check(corpus.S("schema") == (settings.Csv ? "csv-functional-mutations/0.1" : "shape-order-mutation-corpus/0.1") && corpus.B("synthetic") && cases.Count >= 1 && cases.Count <= (settings.Csv ? 30 : 40) && cases.Select(c => c.S("id")).Distinct(StringComparer.Ordinal).Count() == cases.Count && cases.All(c => MyRegex.IsMatch(c.S("id"))), "Unsupported mutation corpus or identities.");
            var sdk = await Processes.Run(["dotnet", "--version"], Root); sdk.Require("SDK inspection"); Json.Check(sdk.Output.Trim() == Sdk, "Exact SDK required."); report["sdk"] = sdk.Output.Trim();
            var count = settings.Csv ? Json.Read(Path.Combine(sample, ".csassay.json"))["release"]!.A("tests")[0].I("minimumExpectedTests") : Json.Read(Path.Combine(sample, "expected-evidence.json"))["tests"].I("Shape.Behavior.Tests");
            var solution = Path.Combine(sample, settings.Solution);
            (await Processes.Run(["dotnet", "restore", solution, "--locked-mode"], Root, Path.Combine(output, "restore.log"))).Require("Sample restore");
            (await Processes.Run(["dotnet", "build", solution, "--no-restore", "-c", "Release", "-m:2", "-nodeReuse:false"], Root, Path.Combine(output, "build.log"))).Require("Sample build");
            var bindings = Bindings(settings); report["inputSha256"] = bindings.DeepClone();
            report["baselineArtifactSha256"] = Artifacts(sample); report["baselines"] = new JsonObject(); report["coverage"] = new JsonObject(); report["isolatedBaselines"] = new JsonObject();
            report["selections"] = Json.Create(("beforeTests", settings.BeforeTests), ("afterTests", count));
            foreach (var phase in new[] { "before", "after" })
            {
                var folder = Path.Combine(output, "baseline", phase); var result = await Tests(settings, sample, folder, phase == "before" ? settings.BeforeTests : count, phase == "before", coverage: true);
                Json.Check(result.S("state") == "Passed", "Original baseline must pass before mutation.");
                report["baselines"]![phase] = result; report["coverage"]![phase] = CoverageRecord(Path.Combine(folder, "coverage.cobertura.xml"), Path.Combine(sample, "src"), settings.Modules);
            }
            foreach (var key in new[] { "lines-valid", "branches-valid" })
            {
                Json.Check(report["coverage"]!["before"].I(key) == report["coverage"]!["after"].I(key), "Coverage denominators differ; selections cannot be compared.");
            }

            if (settings.Csv)
            {
                var demo = await Processes.Run(["dotnet", Path.Combine(sample, "src/CsvFunctional.Cli/bin/Release/net11.0/CsvFunctional.Cli.dll"), Path.Combine(sample, "fixtures/mixed.csv")], Root, Path.Combine(output, "cli-demo.log"));
                var events = demo.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Json.Parse).ToArray();
                Json.Check(demo.ExitCode == 2 && events.Select(e => e.S("status")).SequenceEqual(["accepted", "rejected", "accepted"]) && events[0].S("Note") == "hello, csv" && events[2].S("Note") == "quote \"inside\"" && events[1].A("issues").Count == 3, "CSV CLI integration projection failed.");
                report["cli"] = Json.Create(("exitCode", demo.ExitCode), ("events", events), ("logSha256", Json.Sha(Path.Combine(output, "cli-demo.log"))));
            }
            using var scratch = new TemporaryWorkspace("assay-sample-");
            var copy = Path.Combine(scratch.Path, settings.RelativePath); CopyTree(sample, copy);
            foreach (var name in BuildFiles)
            {
                File.Copy(Path.Combine(Root, name), Path.Combine(scratch.Path, name));
            }

            File.Copy(Path.Combine(Root, "NuGet.config"), Path.Combine(scratch.Path, "NuGet.config"));
            // The isolated project must use the explicit original feed/cache, not a relative nonexistent feed.
            var config = XDocument.Load(Path.Combine(scratch.Path, "NuGet.config"));
            foreach (var add in config.Descendants("packageSources").Elements("add"))
            {
                if ((string?)add.Attribute("key") == "assay-local")
                {
                    add.SetAttributeValue("value", Path.Combine(Root, ".packages"));
                }
            }

            var cache = config.Descendants("config").Elements("add").FirstOrDefault(x => (string?)x.Attribute("key") == "globalPackagesFolder"); cache?.SetAttributeValue("value", Path.Combine(Root, ".packages/cache"));
            config.Save(Path.Combine(scratch.Path, "NuGet.config"));
            (await Processes.Run(["dotnet", "restore", Path.Combine(copy, settings.Solution), "--locked-mode"], scratch.Path, Path.Combine(output, "isolated-restore.log"))).Require("Isolated sample restore");
            async Task<Execution> Build(string log)
            {
                return await Processes.Run(["dotnet", "build", Path.Combine(copy, settings.Solution), "--no-restore", "-c", "Release", "-m:1", "-nodeReuse:false"], scratch.Path, log);
            }

            (await Build(Path.Combine(output, "isolated-build.log"))).Require("Isolated original build");
            foreach (var phase in new[] { "before", "after" })
            {
                var result = await Tests(settings, copy, Path.Combine(output, "isolated-baseline", phase), phase == "before" ? settings.BeforeTests : count, phase == "before"); Json.Check(result.S("state") == "Passed", "Isolated original baseline must pass."); report["isolatedBaselines"]![phase] = result;
            }
            foreach (var item in cases)
            {
                var current = item!; var relative = current.S(settings.Csv ? "path" : "file");
                var target = Inside(Path.Combine(copy, "src"), Path.GetRelativePath("src", relative)); Json.Check(Path.GetExtension(target) == ".cs", "Mutation must target production C#.");
                var original = File.ReadAllBytes(target); var text = Encoding.UTF8.GetString(original); var changed = ExactEdit(text, current);
                var line = text[..text.IndexOf(current.S("before"), StringComparison.Ordinal)].Count(c => c == '\n') + 1;
                if (settings.Csv)
                {
                    Json.Check(line == current.I("line"), "Reviewed mutation line binding changed.");
                }

                var folder = Path.Combine(output, settings.Csv ? "mutations" : "cases", current.S("id"));
                try
                {
                    File.WriteAllText(target, changed); (await Build(Path.Combine(folder, "build.log"))).Require("Declared valid mutation " + current.S("id"));
                    var row = Json.Create(("id", current.S("id")), ("requirement", current.S("requirement")), ("sourceSha256", settings.Csv ? Json.Sha(target) : Convert.ToHexStringLower(SHA256.HashData(original))), ("mutantSha256", Json.Sha(target)), ("location", relative + ":" + line), ("buildLogSha256", Json.Sha(Path.Combine(folder, "build.log"))), ("artifactSha256", Artifacts(copy)), ("phases", new JsonObject()), ("area", current.S("area")));
                    foreach (var phase in new[] { "before", "after" })
                    {
                        var execution = await Tests(settings, copy, Path.Combine(folder, phase), phase == "before" ? settings.BeforeTests : count, phase == "before");
                        var survived = execution.I("failed") == 0; var state = survived ? "survived" : "killed"; var location = relative + ":" + line; var hits = report["coverage"]![phase]!["lineHits"]!;
                        bool? covered = hits[location] is not null ? hits.I(location) > 0 : null;
                        var packet = Json.Create(("schema", "playground-mutation-experiment/0.1"), ("synthetic", true), ("task", settings.Csv ? "csvhelper-functional-investigation" : "functional-shape-order-investigation"), ("investigationContext", current.S("requirement")), ("sourceSha256", row.S("sourceSha256")), ("caseId", current.S("id")), ("baselinePassed", true), ("mutantCompiled", true), ("testsCompleted", true), ("survived", survived), ("covered", covered), ("hasBoundaryAssertion", phase == "after" || (!settings.Csv && current.B("beforeHasAssertion"))), ("testEvidence", execution), ("inputSha256", bindings), ("survivorDisposition", survived ? "unresolved" : "killed_by_selected_tests"), ("assertionBasis", "Declared fixture assertions; independent adjudication remains separate."));
                        var packetPath = Path.Combine(folder, phase, "experiment.json"); Json.Write(packetPath, packet); var advice = await AdviceFor(packetPath);
                        row["phases"]![phase] = Json.Create(("state", state), ("testExecution", execution), ("covered", covered), ("advice", advice));
                        row[phase] = execution.DeepClone(); row[phase + "Advice"] = advice.DeepClone(); row["outcome" + (phase == "before" ? "Before" : "After")] = state;
                    }
                    Json.Check(row["phases"]!["after"].S("state") == current.S("expectedAfter"), "Observed outcome differs from expectation: " + current.S("id"));
                    row["gapDetectedByAddedTests"] = row.S("outcomeBefore") == "survived" && row.S("outcomeAfter") == "killed"; row["survivorDisposition"] = row.S("outcomeAfter") == "survived" ? "unresolved" : "killed_by_declared_tests";
                    report.A("results").Add(row); Json.Write(reportPath, report); Console.WriteLine(current.S("id") + ": " + row.S("outcomeBefore") + " -> " + row.S("outcomeAfter"));
                }
                finally { File.WriteAllBytes(target, original); }
            }
            AssertBindings(bindings); Json.Check(JsonNode.DeepEquals(Bindings(settings), bindings), "Bound input inventory changed.");
            var rows = report.A("results");
            report["summary"] = settings.Csv ? Json.Create(("before", Json.Create(("killed", rows.Count(r => r.S("outcomeBefore") == "killed")), ("survived", rows.Count(r => r.S("outcomeBefore") == "survived")))), ("after", Json.Create(("killed", rows.Count(r => r.S("outcomeAfter") == "killed")), ("survived", rows.Count(r => r.S("outcomeAfter") == "survived"))))) : Json.Create(("cases", rows.Count), ("baselineTests", settings.BeforeTests), ("upliftTests", count), ("killedBefore", rows.Count(r => r.S("outcomeBefore") == "killed")), ("killedAfter", rows.Count(r => r.S("outcomeAfter") == "killed")), ("newlyDetected", rows.Count(r => r.B("gapDetectedByAddedTests"))), ("unresolvedSurvivors", rows.Where(r => r.S("outcomeAfter") == "survived").Select(r => r.S("id"))));
            report["executionStatus"] = "completed"; report["providerComparison"] = "not_requested_offline_run"; Json.Write(reportPath, report); return report;
        }
        catch (Exception failure) { report["executionStatus"] = "failed"; report["failure"] = failure.Message; Json.Write(reportPath, report); throw; }
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex MyRegex { get; }
}
