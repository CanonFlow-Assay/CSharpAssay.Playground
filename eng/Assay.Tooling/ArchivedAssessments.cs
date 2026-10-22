using System.Formats.Tar;
namespace Assay.Tooling;

public static partial class ArchivedAssessments
{
    private static string Shop => Path.Combine(Root, "samples/20-eshop-agent-assay");
    private static JsonObject FindingCounts(JsonNode evidence)
    {
        var counts = new JsonObject(); foreach (var group in evidence.A("findings").GroupBy(f => f.S("ruleId"), StringComparer.Ordinal))
        {
            counts[group.Key] = group.Count();
        }

        return counts;
    }
    public static void ShopEvidence()
    {
        var expected = Json.Read(Path.Combine(Shop, "expectations.json")); var provenance = Json.Read(Path.Combine(Shop, "provenance.json")); var output = Path.Combine(Root, "evidence/generated/eshop-agent"); var report = Json.Read(Path.Combine(output, "check.json")); var evidence = report["evidence"]!;
        Json.Check(report.S("verdict") == expected.S("verdict") && report.I("exitCode") == 1 && evidence.S("toolVersion") == expected.S("toolVersion") && evidence.B("authoritative") == expected.B("authoritative") && evidence.A("projects").Count == expected.I("projects") && evidence.A("findings").Count == expected.I("findings") && evidence.A("missingEvidence").Count == expected.I("missingEvidence") && evidence.A("failures").Count == expected.I("toolFailures") && JsonNode.DeepEquals(FindingCounts(evidence), expected["ruleFindingCounts"]), "Archived eShop evidence differs from its reviewed historical baseline.");
        Json.Check(File.ReadAllText(Path.Combine(output, "upstream-commit.txt")).Trim() == provenance.S("commit") && provenance.S("toolCommit") == "f5fb8e7dd27da20f6d5c26306dc0e56823e37962", "Archived eShop source/tool commit changed.");
    }
    private static string[] RequiredMembers(string source)
    {
        var match = MyRegex.Match(source); Json.Check(match.Success, "Expected class declaration.");
        return Regex.Matches(source, "\\[Required\\]\\s+(?:public|private)\\s+[^;{=]+\\s+(_[A-Za-z]\\w*|[A-Za-z]\\w*)\\s*(?:[;{])").Select(m => match.Groups[1].Value + "." + m.Groups[1].Value).ToArray();
    }
    private static async Task<string> PinnedFile(string repo, JsonNode entry)
    {
        var path = entry.S("sourcePath"); Json.Check(await Processes.Git(repo, "rev-parse", "HEAD:" + path) == entry.S("gitBlob"), "Pinned Git blob changed: " + path);
        // Read raw Git bytes; text decoding must not alter BOM/newlines before hashing.
        using var process = new Process { StartInfo = new ProcessStartInfo("git") { WorkingDirectory = repo, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var arg in new[] { "show", "HEAD:" + path })
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        _ = process.Start(); var error = process.StandardError.ReadToEndAsync(); using var buffer = new MemoryStream(); await process.StandardOutput.BaseStream.CopyToAsync(buffer); await process.WaitForExitAsync(); _ = await error;
        Json.Check(process.ExitCode == 0 && Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())) == entry.S("sha256"), "Pinned SHA-256 changed: " + path); return Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
    }
    public static async Task ShopOrdering(string repo)
    {
        repo = Path.GetFullPath(repo); var expected = Json.Read(Path.Combine(Root, "evidence/expectations/eshop-ordering-domain.json")); Json.Check(await Processes.Git(repo, "rev-parse", "HEAD") == expected.S("commit"), "eShop HEAD is not the reviewed commit."); var count = 0;
        foreach (var entry in expected.A("files")) { var members = RequiredMembers(await PinnedFile(repo, entry!)); Json.Check(members.SequenceEqual(entry.Strings("requiredMembers")), "Required member inventory changed."); count += members.Length; }
        Json.Check(count == 6, "Ordering Required attribute inventory changed.");
    }
    public static async Task ShopCandidate(string repo)
    {
        repo = Path.GetFullPath(repo); var expected = Json.Read(Path.Combine(Shop, "candidate-expectations.json")); var provenance = Json.Read(Path.Combine(Shop, "provenance.json")); var baseline = Json.Read(Path.Combine(Root, "evidence/expectations/eshop-ordering-domain.json")); var output = Path.Combine(Root, "evidence/generated/eshop-candidate"); var report = Json.Read(Path.Combine(output, "check.json")); var candidate = provenance["candidate"]!;
        var commit = await Processes.Git(repo, "rev-parse", "HEAD"); Json.Check(commit == candidate.S("commit") && File.ReadAllText(Path.Combine(output, "candidate-commit.txt")).Trim() == commit, "Candidate source/recorded commit changed.");
        foreach (var entry in candidate.A("files"))
        {
            var source = await PinnedFile(repo, entry!); if (entry.S("sourcePath").StartsWith("src/Ordering.Domain/", StringComparison.Ordinal))
            {
                Json.Check(RequiredMembers(source).Length == 0, "Candidate retains Required attributes.");
            }
        }
        var members = baseline.A("files").SelectMany(e => e.Strings("requiredMembers")).ToArray(); Json.Check(members.SequenceEqual(expected.Strings("removedRequiredMembers")) && members.Length == expected.I("removedRequiredAttributes"), "Base Required inventory changed.");
        var trx = XDocument.Load(Path.Combine(output, "test-results/ordering-candidate.trx")); var counters = trx.Descendants().Where(e => e.Name.LocalName == "Counters").ToArray(); Json.Check(counters.Length == 1, "Expected one TRX counter selection."); foreach (var (key, value) in expected["trxCounts"]!.AsObject())
        {
            Json.Check((string?)counters[0].Attribute(key) == value!.ToJsonString(), "Wrong TRX counter: " + key);
        }

        var results = trx.Descendants().Where(e => e.Name.LocalName == "UnitTestResult").ToArray(); Json.Check(results.Length == expected["trxCounts"].I("total") && expected.Strings("requiredMappingTests").All(n => results.Any(r => (string?)r.Attribute("testName") == n && (string?)r.Attribute("outcome") == "Passed")), "Missing candidate executions/mapping tests.");
        var evidence = report["evidence"]!; var normalized = Json.Array(evidence.A("findings").Select(f => Json.Create(("ruleId", f.S("ruleId")), ("path", f!["location"].S("path")), ("line", f["location"].I("startLine")), ("column", f["location"].I("startColumn")), ("message", f.S("message")))));
        Json.Check(report.S("verdict") == expected.S("verdict") && report.I("exitCode") == expected.I("exitCode") && evidence.S("toolVersion") == expected.S("toolVersion") && evidence.B("authoritative") == expected.B("authoritative") && evidence.A("projects").Count == expected.I("projects") && evidence.A("missingEvidence").Count == 0 && evidence.A("failures").Count == 0 && JsonNode.DeepEquals(normalized, expected["findings"]), "Candidate evidence/finding inventory changed.");
    }
    public static JsonObject UpstreamTests(string path)
    {
        var tree = XDocument.Load(path); var counter = tree.Descendants().Single(e => e.Name.LocalName == "Counters"); var result = new JsonObject();
        foreach (var key in new[] { "total", "executed", "passed", "failed", "error", "timeout", "aborted", "notExecuted" })
        {
            result[key] = (int?)counter.Attribute(key) ?? -1;
        }

        var tests = tree.Descendants().Where(e => e.Name.LocalName == "UnitTestResult").ToArray();
        Json.Check(result.I("total") == 1063 && result.I("executed") == result.I("total") && result.I("passed") + result.I("failed") == result.I("total") && sourceArray.All(k => result.I(k) == 0) && tests.Length == result.I("total"), "Incomplete or changed upstream test selection.");
        var failed = tests.Where(t => (string?)t.Attribute("outcome") == "Failed").Select(t => (string)t.Attribute("testName")!).Order(StringComparer.Ordinal).ToArray(); Json.Check(failed.Length == result.I("failed"), "Upstream failures are not accounted for.");
        result["baselinePassed"] = failed.Length == 0; result["failedTests"] = Json.Array(failed); result["reportSha256"] = Json.Sha(path); result["targetFramework"] = "net9.0"; result["runtimePolicy"] = "DOTNET_ROLL_FORWARD=Major; actual test-host runtime revision not observed"; result["scope"] = "Original upstream Linux target, not its Windows/multi-target matrix."; return result;
    }
    public static async Task<JsonObject> CsvUpstream(Arguments args)
    {
        var sample = Path.Combine(Root, SampleInvestigations.Csv.RelativePath); var output = Path.GetFullPath(args.Get("--output", Path.Combine(Root, "evidence/generated/csvhelper"))); var upstream = Path.Combine(output, "upstream"); var cache = Path.Combine(Root, ".upstream-cache/CsvHelper"); var pin = Json.Read(Path.Combine(sample, "upstream.json")); var path = Path.Combine(output, "upstream-assessment.json");
        var report = Json.Create(("schema", "csvhelper-upstream-assessment/0.1"), ("executionStatus", "running"), ("source", pin), ("providerRequests", 0), ("authority", "Original upstream assessment, not an authored .NET11 sample or full-library migration.")); Json.Write(path, report);
        try
        {
            if (!Directory.Exists(cache))
            {
                Json.Check(args.Has("--fetch"), "Pinned local checkout missing; explicit --fetch permits one exact-commit fetch."); _ = Directory.CreateDirectory(cache); _ = await Processes.Git(cache, "init", "-q");
                (await Processes.Run(["git", "fetch", "--depth=1", pin.S("repository"), pin.S("commit")], cache, Path.Combine(output, "fetch.log"), 300)).Require("Single pinned fetch; stop on errors/restrictions, no retry"); _ = await Processes.Git(cache, "checkout", "--detach", "-q", "FETCH_HEAD");
            }
            Json.Check(await Processes.Git(cache, "rev-parse", "HEAD") == pin.S("commit") && (await Processes.Git(cache, "status", "--porcelain")).Length == 0, "Upstream must remain clean at pinned commit.");
            var paths = (await Processes.Git(cache, "ls-files")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(p => p.StartsWith("src/CsvHelper/", StringComparison.Ordinal) || p.StartsWith("tests/CsvHelper.Tests/", StringComparison.Ordinal) || p is "LICENSE.txt" or "README.markdown" or ".editorconfig").ToArray(); var sources = new JsonObject(); foreach (var file in paths)
            {
                sources[file] = Json.Sha(Inside(cache, file));
            }

            foreach (var (name, checksum) in pin["derivedFiles"]!.AsObject())
            {
                Json.Check(sources.S(name) == checksum!.GetValue<string>(), "Derived algorithm source changed.");
            }

            report["sourceSha256"] = sources.DeepClone();
            if (args.Has("--run"))
            {
                Json.Check(!Directory.Exists(upstream), "Choose a fresh upstream output; prior evidence is retained."); _ = Directory.CreateDirectory(upstream); var archive = Path.Combine(upstream, "source.tar"); _ = await Processes.Git(cache, "archive", "--format=tar", "--output=" + archive, "HEAD"); TarFile.ExtractToDirectory(archive, upstream, overwriteFiles: false); File.Delete(archive);
                Json.Write(Path.Combine(upstream, "global.json"), Json.Create(("sdk", Json.Read(Path.Combine(Root, "global.json"))["sdk"])));
                foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets" })
                {
                    File.WriteAllText(Path.Combine(upstream, name), "<Project />\n");
                }

                File.WriteAllText(Path.Combine(upstream, "Directory.Packages.props"), "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>\n"); File.WriteAllText(Path.Combine(upstream, "NuGet.Config"), "<configuration><packageSources><clear/><add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" /></packageSources></configuration>");
                const string project = "tests/CsvHelper.Tests/CsvHelper.Tests.csproj";
                (await Processes.Run(["dotnet", "restore", project, "-p:TargetFrameworks=net9.0", "--configfile", "NuGet.Config"], upstream, Path.Combine(upstream, "restore.log"), 300)).Require("Original target restore");
                (await Processes.Run(["dotnet", "build", project, "-p:TargetFrameworks=net9.0", "--no-restore", "-c", "Release"], upstream, Path.Combine(upstream, "build.log"), 300)).Require("Original target build");
                var tests = await Processes.Run(["dotnet", "test", project, "-p:TargetFrameworks=net9.0", "--framework", "net9.0", "--no-restore", "--no-build", "-c", "Release", "--logger", "trx", "--results-directory", "results"], upstream, Path.Combine(upstream, "tests.log"), 300, new Dictionary<string, string> { ["DOTNET_ROLL_FORWARD"] = "Major" }); report["upstreamTestExitCode"] = tests.ExitCode;
                var policy = Json.Read(Path.Combine(sample, ".csassay.json")); policy["release"]!["tests"] = new JsonArray(); policy["boundaries"] = Json.Create(("coreProjects", values), ("shellProjects", Array.Empty<string>()), ("coreNamespaces", valuesArray), ("shellNamespaces", Array.Empty<string>())); policy["representations"] = Json.Create(("resultTypes", Array.Empty<string>()), ("optionTypes", Array.Empty<string>()), ("closedTypes", Array.Empty<string>())); Json.Write(Path.Combine(upstream, "native-policy.json"), policy);
                _ = await Processes.Run(["dotnet", Engine, "check", Path.Combine(upstream, "src/CsvHelper/CsvHelper.csproj"), "--policy", Path.Combine(upstream, "native-policy.json"), "--json", Path.Combine(upstream, "native-check.json")], Root, Path.Combine(upstream, "native-check.log"), 300);
            }
            foreach (var (name, checksum) in sources)
            {
                Json.Check(File.Exists(Inside(upstream, name)) && Json.Sha(Inside(upstream, name)) == checksum!.GetValue<string>(), "Exported original source differs from pin.");
            }

            var reports = Directory.GetFiles(Path.Combine(upstream, "results"), "*.trx"); Json.Check(reports.Length == 1 && File.ReadAllText(Path.Combine(upstream, "build.log")).Contains("Build succeeded.", StringComparison.Ordinal), "Single complete baseline build/test report required."); var observed = UpstreamTests(reports[0]); report["tests"] = observed;
            var native = Json.Read(Path.Combine(upstream, "native-check.json")); var evidence = native["evidence"]!; report["nativeCheck"] = Json.Create(("verdict", native.S("verdict")), ("authoritative", evidence.B("authoritative")), ("missingEvidenceCodes", evidence.A("missingEvidence").Select(e => e.S("code")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)), ("failureCodes", evidence.A("failures").Select(e => e.S("code")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)), ("receiptSha256", Json.Sha(Path.Combine(upstream, "native-check.json"))));
            var packet = Json.Create(("schema", "playground-mutation-experiment/0.1"), ("synthetic", true), ("task", "csvhelper-upstream-baseline-review"), ("sourceSha256", Json.Digest(sources)), ("baselinePassed", observed.B("baselinePassed")), ("mutantCompiled", false), ("testsCompleted", true), ("survived", false), ("covered", null), ("hasBoundaryAssertion", null)); var packetPath = Path.Combine(output, "upstream-baseline-experiment.json"); Json.Write(packetPath, packet); var advice = await SampleInvestigations.AdviceFor(packetPath); Json.Check(advice.S("action") == "HumanReview", "Incomplete original evidence must request human review."); report["decisionAssist"] = advice; Json.Write(Path.Combine(output, "upstream-baseline-advice.json"), advice); report["executionStatus"] = "completed-assessment"; report["assessmentToolSha256"] = Json.Sha(Tool); Json.Write(path, report); return report;
        }
        catch (Exception error) { report["executionStatus"] = "failed"; report["failure"] = error.Message; Json.Write(path, report); throw; }
    }

    [GeneratedRegex("(?:public\\s+)?class\\s+(\\w+)")]
    private static partial Regex MyRegex { get; }

    private static readonly string[] valuesArray = new[] { "CsvHelper" };
    private static readonly string[] values = new[] { "src/CsvHelper/CsvHelper.csproj" };
    private static readonly string[] sourceArray = new[] { "error", "timeout", "aborted", "notExecuted" };
}
