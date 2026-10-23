namespace Assay.Tooling;

public sealed record ApplicationSpec(string Root, JsonNode Manifest, JsonNode Requirements);
public static partial class ApplicationRunner
{
    private static readonly HashSet<string> Suffixes = [".cs", ".csproj", ".slnx", ".sln", ".json", ".props", ".targets", ".config", ".sh", ".xml"];
    private static readonly string[] sourceArray = new[] { "total", "passed", "failed", "skipped", "errors", "not-run" };
    private static readonly string[] RequiredSourceInputs = ["solution", "policy", "requirements", "testProject", "contractBaseline", "mutations"];

    public static ApplicationSpec Load(string path)
    {
        path = Path.GetFullPath(path);
        var manifest = Json.Read(path);
        Json.Check(manifest.S("schema") is "assay-project/0.1" or "assay-project/0.2" && MyRegex.IsMatch(manifest.S("id")), "Unsupported manifest or unsafe application identity.");
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, manifest.S("root")));
        foreach (var name in new[] { "solution", "policy", "requirements", "testProject", "testDll", "workerDll", "contractBaseline", "mutations" })
        {
            _ = Inside(root, manifest.S(name));
        }

        Json.Check(manifest.I("minimumTests") is >= 1 and <= 100000 && manifest.I("maximumMutations") is >= 1 and <= 12, "Invalid execution budget.");
        _ = ReportArguments(manifest);
        Json.Check(manifest.S("coverageProvider", "microsoft-mtp") is "microsoft-mtp" or "coverlet-mtp", "Unsupported coverage provider.");
        if (manifest.S("coverageProvider") == "coverlet-mtp")
        {
            var modules = manifest.Strings("coverageModules");
            Json.Check(modules.Length > 0 && modules.Distinct(StringComparer.Ordinal).Count() == modules.Length && modules.All(m => Regex.IsMatch(m, "^[A-Za-z][A-Za-z0-9_.-]{0,127}$")), "Declare unique production coverage modules.");
        }
        var requirements = Json.Read(Inside(root, manifest.S("requirements")));
        var rows = requirements.A("requirements");
        Json.Check(requirements.S("schema") == "assay-requirements/0.1" && rows.Count > 0 && rows.Select(r => r.S("id")).Distinct(StringComparer.Ordinal).Count() == rows.Count && rows.All(r => !string.IsNullOrWhiteSpace(r.S("description")) && r?["probeIds"] is JsonArray), "Incomplete or duplicate requirements.");
        foreach (var input in manifest.Strings("inputs"))
        {
            _ = Inside(root, input);
        }

        _ = WorkerProtocol.Specification(manifest, requirements);
        return new(root, manifest, requirements);
    }
    public static JsonObject Snapshot(string root, JsonNode manifest)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in RequiredSourceInputs.Where(key => manifest[key] is not null))
        {
            var path = Inside(root, manifest.S(key));
            Json.Check(File.Exists(path), "Missing mandatory application input: " + key);
            paths.Add(path);
        }
        foreach (var name in manifest.Strings("inputs"))
        {
            var entry = Inside(root, name);
            Json.Check(File.Exists(entry) || Directory.Exists(entry), "Missing declared input: " + name);
            foreach (var path in File.Exists(entry) ? [entry] : Files(entry))
            {
                if ((Suffixes.Contains(Path.GetExtension(path)) || Path.GetFileName(path) == ".editorconfig") && !Relative(root, path).Split('/').Any(Excluded.Contains))
                {
                    _ = paths.Add(Inside(root, Relative(root, path)));
                }
            }
        }
        Json.Check(paths.Count > 0, "Empty source snapshot.");
        var result = new JsonObject();
        foreach (var path in paths)
        {
            result[Relative(root, path)] = Json.Sha(path);
        }

        return result;
    }
    public static JsonObject ApplicationArtifacts(string root, JsonNode manifest)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in new[] { "testDll", "workerDll" })
        {
            var entry = Inside(root, manifest.S(key));
            Json.Check(File.Exists(entry), "Compiled application entry is missing: " + key);
            foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(entry)!, "*", SearchOption.AllDirectories))
            {
                paths.Add(Inside(root, Relative(root, path)));
            }
        }
        var result = new JsonObject();
        foreach (var path in paths) { result[Relative(root, path)] = Json.Sha(path); }
        return result;
    }
    public static JsonObject ToolkitIdentity()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal) { Engine, Decision, Tool };
        foreach (var folder in new[] { Path.GetDirectoryName(Engine)!, Path.GetDirectoryName(Decision)!, Path.GetDirectoryName(Tool)! })
        {
            foreach (var path in Directory.EnumerateFiles(folder).Where(p => Path.GetExtension(p) is ".dll" or ".json"))
            {
                _ = paths.Add(path);
            }
        }

        foreach (var path in Files(Path.Combine(Root, "eng/Assay.Tooling")).Where(p => Path.GetExtension(p) is ".cs" or ".csproj"))
        {
            _ = paths.Add(path);
        }

        var result = new JsonObject();
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            result[path] = Json.Sha(path);
        }

        return result;
    }
    public static async Task<JsonObject> Intake(string root, JsonNode manifest, string output)
    {
        _ = Directory.CreateDirectory(output);
        var sdk = await Processes.Run(["dotnet", "--version"], root, Path.Combine(output, "sdk.log"), 30);
        var solution = Inside(root, manifest.S("solution"));
        var names = Path.GetExtension(solution) switch
        {
            ".slnx" => XDocument.Load(solution).Descendants("Project").Select(p => (string?)p.Attribute("Path") ?? throw new InvalidDataException("Missing project path.")).ToArray(),
            ".csproj" => [Path.GetFileName(solution)],
            _ => throw new InvalidDataException("Intake requires an explicit csproj or slnx; no automatic solution conversion.")
        };
        var projects = new JsonArray();
        for (var index = 0; index < names.Length; index++)
        {
            var project = Inside(root, Path.Combine(Path.GetDirectoryName(Relative(root, solution))!, names[index].Replace('\\', '/')));
            var result = await Processes.Run(["dotnet", "msbuild", project, "-nologo", "-getProperty:TargetFramework", "-getProperty:TargetFrameworks"], root, Path.Combine(output, "project-" + index + ".log"), 60);
            string[] frameworks = [];
            try { var props = Json.Parse(result.Output)["Properties"]; frameworks = (props.S("TargetFrameworks") is { Length: > 0 } tfms ? tfms : props.S("TargetFramework")).Split(';', StringSplitOptions.RemoveEmptyEntries); }
            catch (JsonException) { }
            projects.Add(Json.Create(("path", Relative(root, project)), ("frameworks", frameworks), ("evaluated", result.State == ProcessState.Passed && frameworks.Length > 0)));
        }
        var support = sdk.State == ProcessState.Passed && sdk.Output.Trim() == Sdk && projects.Count > 0 && projects.All(p => p.B("evaluated")) ? projects.All(p => p.Strings("frameworks").All(t => Regex.IsMatch(t, "^net11\\.0(?:-.+)?$"))) ? "Native" : "Unsupported" : "Incomplete";
        return Json.Create(("schema", "assay-project-intake/0.1"), ("support", support), ("sdk", sdk.Output.Trim()), ("requiredSdk", Sdk), ("projects", projects), ("scope", "Evaluated declared projects. CSharpAssay determines authoritative graph/compiler completeness; no automatic retargeting."));
    }
    public static string[] ReportArguments(JsonNode manifest)
    {
        var option = manifest.S("testReportProtocol", "xunit-mtp-v1") switch { "xunit-mtp-v1" => "--report-xunit", "xunit-mtp-v2" => "--report-xunit-xml", _ => throw new InvalidDataException("Unsupported test report protocol; no command selected.") };
        return [option, option + "-filename", "tests.xml"];
    }
    public static JsonObject TestRecord(string path, Execution execution, int minimum, int? expected = null, string[]? expectedNames = null)
    {
        if (execution.State is ProcessState.TimedOut or ProcessState.Cancelled)
        {
            return Json.Create(("state", execution.StateName), ("exitCode", execution.ExitCode));
        }

        try
        {
            Json.Check(File.Exists(path), "Missing xUnit report.");
            var assemblies = XDocument.Load(path).Root!.Elements("assembly").ToArray();
            var tests = assemblies.SelectMany(a => a.Descendants("test")).ToArray();
            var counts = sourceArray.ToDictionary(k => k, k => assemblies.Sum(a => (int?)a.Attribute(k) ?? 0), StringComparer.Ordinal);
            var names = tests.Select(t => (string?)t.Attribute("name") ?? "").Order(StringComparer.Ordinal).ToArray();
            Json.Check(assemblies.Length == 1 && counts["total"] >= minimum && execution.ExitCode is 0 or 2 && counts["total"] == tests.Length && counts["total"] == counts["passed"] + counts["failed"] + counts["skipped"] && counts["skipped"] == 0 && counts["errors"] == 0 && counts["not-run"] == 0 && (expected is null || counts["total"] == expected), "Incomplete or changed test selection.");
            Json.Check(tests.All(t => (string?)t.Attribute("result") is "Pass" or "Fail") && counts["passed"] == tests.Count(t => (string?)t.Attribute("result") == "Pass") && counts["failed"] == tests.Count(t => (string?)t.Attribute("result") == "Fail"), "Contradictory individual test outcomes.");
            Json.Check(names.All(n => n.Length > 0) && names.Distinct(StringComparer.Ordinal).Count() == names.Length && execution.ExitCode == 0 == (counts["failed"] == 0) && (expectedNames is null || names.SequenceEqual(expectedNames)), "Ambiguous test selection or execution status.");
            var result = Json.Create(("state", counts["failed"] > 0 ? "Failed" : "Passed"), ("testNames", names), ("reportSha256", Json.Sha(path)), ("exitCode", execution.ExitCode));
            foreach (var (key, value) in counts)
            {
                result[key] = value;
            }

            result["failedTests"] = Json.Array(tests.Where(t => (string?)t.Attribute("result") == "Fail").Select(t => (string?)t.Attribute("name") ?? ""));
            return result;
        }
        catch (Exception failure) when (failure is InvalidDataException or System.Xml.XmlException or FormatException or OverflowException)
        { return Json.Create(("state", "ToolFailure"), ("reason", "Invalid, absent or incomplete xUnit evidence: " + failure.Message), ("exitCode", execution.ExitCode)); }
    }
    public static JsonObject NormalizeCoverage(string output, string[] modules)
    {
        var canonical = Path.Combine(output, "coverage.cobertura.xml");
        var reports = Directory.EnumerateFiles(output, "*cobertura*.xml").Where(p => p != canonical).ToArray();
        Json.Check(reports.Length == 1, "A unique fresh Coverlet report is required.");
        var tree = XDocument.Load(reports[0]);
        var observed = tree.Root!.Element("packages")!.Elements("package").Select(p => (string?)p.Attribute("name") ?? "").ToArray();
        Json.Check(modules.Length > 0 && observed.Distinct(StringComparer.Ordinal).Count() == observed.Length && observed.ToHashSet(StringComparer.Ordinal).SetEquals(modules) && (int?)tree.Root.Attribute("lines-valid") > 0, "Coverage is empty or differs from production module scope.");
        File.Copy(reports[0], canonical, overwrite: true);
        return Json.Create(("provider", "coverlet.MTP"), ("modules", observed.Order(StringComparer.Ordinal)), ("rawReport", Path.GetFileName(reports[0])), ("sha256", Json.Sha(canonical)));
    }
    public static async Task<JsonObject> Tests(string root, JsonNode manifest, string output, int? expected = null, bool coverage = false, string[]? expectedNames = null, string? selectedClass = null)
    {
        var protocol = ReportArguments(manifest);
        var provider = manifest.S("coverageProvider", "microsoft-mtp");
        Json.Check(provider is "microsoft-mtp" or "coverlet-mtp", "Unsupported coverage provider.");
        _ = Directory.CreateDirectory(output);
        File.Delete(Path.Combine(output, "tests.xml"));
        if (coverage)
        {
            foreach (var file in Directory.EnumerateFiles(output, "*cobertura*.xml"))
            {
                File.Delete(file);
            }
        }

        List<string> args = ["dotnet", Inside(root, manifest.S("testDll")), "--minimum-expected-tests", manifest.I("minimumTests").ToString(CultureInfo.InvariantCulture), .. protocol, "--results-directory", output];
        if (selectedClass is not null) { args.Add("--filter-class"); args.Add(selectedClass); }
        if (coverage)
        {
            args.AddRange(provider == "coverlet-mtp" ? ["--coverlet", "--coverlet-output-format", "cobertura"] : ["--coverage", "--coverage-output", Path.Combine(output, "coverage.cobertura.xml"), "--coverage-output-format", "cobertura"]);
        }

        var execution = await Processes.Run(args, root, Path.Combine(output, "execution.log"), 120);
        var result = TestRecord(Path.Combine(output, "tests.xml"), execution, manifest.I("minimumTests"), expected, expectedNames);
        if (coverage && result.S("state") is "Passed" or "Failed" && provider == "coverlet-mtp")
        {
            try { result["coverage"] = NormalizeCoverage(output, manifest.Strings("coverageModules")); }
            catch (Exception failure) when (failure is InvalidDataException or IOException or System.Xml.XmlException) { return Json.Create(("state", "ToolFailure"), ("reason", failure.Message), ("execution", result)); }
        }
        return result;
    }
    public static async Task<JsonObject> Mutations(string root, JsonNode manifest, string output, int baselineTotal)
    {
        var corpus = Json.Read(Inside(root, manifest.S("mutations")));
        var cases = corpus.A("cases");
        Json.Check(corpus.S("schema") == "assay-mutations/0.1" && cases.Count >= 1 && cases.Count <= manifest.I("maximumMutations") && cases.Select(c => c.S("id")).Distinct(StringComparer.Ordinal).Count() == cases.Count, "Invalid mutation corpus or budget.");
        _ = Directory.CreateDirectory(output);
        using var scratch = new TemporaryWorkspace("assay-application-");
        var copy = Path.Combine(scratch.Path, "project");
        CopyTree(root, copy);
        (await Processes.Run(["dotnet", "restore", Inside(copy, manifest.S("solution")), "--locked-mode", "--configfile", Path.Combine(root, "NuGet.config")], copy, Path.Combine(output, "restore.log"))).Require("Isolated restore");
        (await Processes.Run(["dotnet", "build", Inside(copy, manifest.S("solution")), "--no-restore", "-c", "Release"], copy, Path.Combine(output, "baseline-build.log"))).Require("Isolated baseline build");
        var isolated = await Tests(copy, manifest, Path.Combine(output, "original"), baselineTotal);
        Json.Check(isolated.S("state") == "Passed", "Isolated original baseline did not pass.");
        var requirementIds = Json.Read(Inside(copy, manifest.S("requirements"))).A("requirements").Select(r => r.S("id")).ToHashSet(StringComparer.Ordinal);
        var records = new JsonArray();
        foreach (var item in cases)
        {
            var current = item!;
            Json.Check(Regex.IsMatch(current.S("id"), "^[a-z0-9-]{1,64}$"), "Unsafe mutation identity.");
            var target = Inside(copy, current.S("path"));
            var production = manifest.Strings("productionRoots");
            if (production.Length == 0)
            {
                production = ["src", "samples/70-polly-resilience/src"];
            }

            Json.Check(Path.GetExtension(target) == ".cs" && production.Any(p => target.StartsWith(Inside(copy, p) + Path.DirectorySeparatorChar, StringComparison.Ordinal)), "Mutation must target production C# source.");
            var original = File.ReadAllBytes(target);
            var text = Encoding.UTF8.GetString(original);
            Json.Check(current.S("before").Length > 0 && text.Split(current.S("before"), StringSplitOptions.None).Length == 2 && requirementIds.Contains(current.S("requirementId")), "Mutation span or requirement is ambiguous.");
            var folder = Path.Combine(output, current.S("id"));
            try
            {
                File.WriteAllText(target, text.Replace(current.S("before"), current.S("after"), StringComparison.Ordinal));
                var sourceHash = Json.Sha(target);
                var build = await Processes.Run(["dotnet", "build", Inside(copy, manifest.S("solution")), "--no-restore", "-c", "Release"], copy, Path.Combine(folder, "build.log"));
                JsonObject execution;
                string state;
                if (build.State is ProcessState.TimedOut or ProcessState.Cancelled) { state = build.StateName; execution = Json.Create(("state", state)); }
                else if (build.State != ProcessState.Passed)
                {
                    // A restore/MSBuild outage is a tool failure, never an invalid C# mutant.
                    var codes = Regex.Matches(build.Output, "\\berror (\\w+):").Select(m => m.Groups[1].Value).Distinct().ToArray();
                    state = build.ExitCode == 1 && codes.Length > 0 && codes.All(c => Regex.IsMatch(c, "^CS[0-9]+$")) ? "Invalid" : "ToolFailure";
                    execution = Json.Create(("state", state));
                }
                else { execution = await Tests(copy, manifest, folder, baselineTotal, expectedNames: isolated.Strings("testNames")); state = execution.S("state") switch { "Passed" => "Survived", "Failed" => "Killed", var other => other }; }
                records.Add(Json.Create(("id", current.S("id")), ("path", current.S("path")), ("requirementId", current.S("requirementId")), ("state", state), ("execution", execution), ("mutatedSourceSha256", sourceHash), ("baselineSourceSha256", Convert.ToHexStringLower(SHA256.HashData(original))), ("buildLogSha256", Json.Sha(Path.Combine(folder, "build.log"))), ("expectedState", current.S("expectedState")), ("excludedAsEquivalent", false)));
                Json.Write(Path.Combine(output, "mutations.json"), Json.Create(("schema", "assay-mutation-preview/0.1"), ("executionStatus", "running"), ("results", records)));
            }
            finally { File.WriteAllBytes(target, original); }
        }
        var report = Json.Create(("schema", "assay-mutation-preview/0.1"), ("executionStatus", "completed"), ("results", records), ("unexpectedOutcomes", records.Where(r => r.S("state") != r.S("expectedState")).Select(r => r.S("id"))), ("authority", "Bounded declared edits; no automatic equivalence exclusion or universal mutation score."));
        Json.Write(Path.Combine(output, "mutations.json"), report);
        return report;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9.-]{0,63}$")]
    private static partial Regex MyRegex { get; }
}
