namespace Assay.Tooling;

public static partial class DecisionExperiments
{
    private const string Assembly = "bin/Release/net11.0/AmountProbe.dll";
    private static string Sample => Path.Combine(Root, "samples/50-decision-assistance");
    private static string Output => Path.Combine(Root, "evidence/generated/decision-assistance");
    public static JsonObject Compilation(Execution result)
    {
        var codes = MyRegex.Matches(result.Output).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (result.State == ProcessState.Passed && codes.Length == 0)
        {
            return Json.Create(("status", "compiled"), ("exitCode", 0), ("diagnostics", codes));
        }

        Json.Check(result.State == ProcessState.Failed && result.ExitCode == 1 && codes.Length > 0 && codes.All(c => Regex.IsMatch(c, "^CS[0-9]+$")), "Unexpected build failure; not a compiler-invalid mutation.");
        return Json.Create(("status", "invalid"), ("exitCode", 1), ("diagnostics", codes));
    }
    public static JsonObject ProbeObservation(Execution result, JsonNode suite)
    {
        Json.Check(result.State is ProcessState.Passed or ProcessState.Failed && result.ExitCode is 0 or 7, "Probe crashed or timed out; not a killed mutant.");
        var value = Json.Parse(result.Output).AsObject();
        Json.Check(value.Select(p => p.Key).ToHashSet(StringComparer.Ordinal).SetEquals(["tests", "failures", "policyCalls"]), "Unexpected probe record.");
        foreach (var (_, count) in value)
        {
            Json.Check(count?.GetValueKind() == JsonValueKind.Number && int.TryParse(count.ToJsonString(), out _), "Probe counts require integers.");
        }

        Json.Check(value.I("tests") == suite.I("tests") && value.I("policyCalls") == suite.I("policyCalls") && value.I("failures") >= 0 && value.I("failures") <= value.I("tests") && result.ExitCode == 0 == (value.I("failures") == 0), "Probe counts/exit contradict the declared selection.");
        value["exitCode"] = result.ExitCode; return value;
    }
    private static async Task<JsonObject> Compile(string target, string source)
    {
        _ = Directory.CreateDirectory(target);
        foreach (var name in SampleInvestigations.BuildFiles.Where(n => n != "Directory.Packages.props"))
        {
            File.Copy(Path.Combine(Root, name), Path.Combine(target, name));
        }

        foreach (var name in new[] { "AmountProbe.csproj", "Program.cs" })
        {
            File.Copy(Path.Combine(Sample, "probe", name), Path.Combine(target, name));
        }

        File.WriteAllText(Path.Combine(target, "Amount.cs"), source); File.WriteAllText(Path.Combine(target, "NuGet.config"), "<configuration><packageSources><clear /></packageSources></configuration>\n");
        (await Processes.Run(["dotnet", "restore", "AmountProbe.csproj", "--configfile", "NuGet.config"], target, Path.Combine(target, "restore.log"), 120)).Require("Isolated probe restore");
        return Compilation(await Processes.Run(["dotnet", "build", "AmountProbe.csproj", "--no-restore", "-c", "Release"], target, Path.Combine(target, "build.log"), 120));
    }
    private static async Task<JsonObject> Observe(string target, string suite, JsonNode suites)
    {
        return ProbeObservation(await Processes.Run(["dotnet", Assembly, suite], target, Path.Combine(target, suite + ".log"), 120), suites[suite]!);
    }

    public static async Task<JsonObject> ExecuteCase(JsonNode item, JsonNode suites, string originalTarget, string scratch, string original)
    {
        var selected = item.S("selectedSuite"); var baseline = await Observe(originalTarget, selected, suites);
        Json.Check(baseline.I("failures") == item.I("expectedBaselineFailures"), "Original baseline differs from case requirement.");
        var record = Json.Create(("baselinePassed", baseline.I("failures") == 0), ("mutantCompiled", false), ("testsCompleted", false), ("survived", false), ("covered", null), ("hasBoundaryAssertion", suites[selected].B("hasBoundaryAssertion")), ("observations", Json.Create(("original-selected", baseline))), ("mutantBuild", Json.Create(("status", "not_run_baseline_failed"))), ("repairDemonstrated", false), ("outcome", "baseline_failed"), ("survivorDisposition", "not_applicable"), ("probeCommands", Json.Array(new[] { new[] { "dotnet", Assembly, selected } })));
        if (!record.B("baselinePassed"))
        {
            return record;
        }

        var target = Inside(scratch, item.S("id")); var build = await Compile(target, original.Replace("amount > 0", item.S("replacement"), StringComparison.Ordinal)); record["mutantBuild"] = build;
        if (build.S("status") == "invalid") { Json.Check(build.Strings("diagnostics").SequenceEqual([item.S("expectedDiagnostic")]), "Invalid mutation failed with unrelated compiler diagnostics."); record["outcome"] = "invalid"; return record; }
        record["mutantCompiled"] = true;
        var observed = await Observe(target, selected, suites); Json.Check(observed.I("failures") == item.I("expectedSelectedFailures"), "Mutant assertion failures differ from expectation.");
        record["observations"]!["mutant-selected"] = observed.DeepClone(); record["testsCompleted"] = true; record["survived"] = observed.I("failures") == 0; record["covered"] = observed.I("policyCalls") > 0; record["outcome"] = record.B("survived") ? "survived" : "killed"; if (record.B("survived"))
        {
            record["survivorDisposition"] = "unresolved";
        }

        if (item["probeSuite"] is not null)
        {
            var suite = item.S("probeSuite"); var before = await Observe(originalTarget, suite, suites); var after = await Observe(target, suite, suites);
            Json.Check(before.I("failures") == 0 && after.I("failures") == item.I("expectedProbeFailures"), "Additional probe did not demonstrate the declared behavior.");
            record["observations"]!["original-probe"] = before; record["observations"]!["mutant-probe"] = after; record["repairDemonstrated"] = record.B("survived") && after.I("failures") > 0;
        }
        return record;
    }
    public static async Task<JsonObject> RunCorpus()
    {
        var path = Path.Combine(Output, "mutation-cases.json"); var report = Json.Create(("schema", "playground-mutation-case-results/0.1"), ("executionStatus", "running"), ("synthetic", true), ("executed", true), ("hypotheticalStates", false), ("results", new JsonArray()), ("authority", "Bounded fixture executions; not model quality or an authoritative verification receipt.")); Json.Write(path, report);
        try
        {
            var sdk = await Processes.Run(["dotnet", "--version"], Root); sdk.Require("SDK inspection"); Json.Check(sdk.Output.Trim() == Sdk, "Exact repository SDK required.");
            var manifest = Path.Combine(Sample, "corpus/mutation-cases.json"); var corpus = Json.Read(manifest); var cases = corpus.A("cases");
            Json.Check(corpus.S("schema") == "playground-executed-mutation-cases/0.1" && corpus.B("synthetic") && cases.Count > 0 && cases.Select(c => c.S("id")).Distinct(StringComparer.Ordinal).Count() == cases.Count && cases.All(c => Regex.IsMatch(c.S("id"), "^[a-z0-9-]+$")), "Unsupported mutation corpus/identities.");
            var source = Path.Combine(Sample, "probe/Amount.cs"); var original = File.ReadAllText(source); Json.Check(original.Split("amount > 0", StringSplitOptions.None).Length == 2, "Expected one mutation site.");
            var identities = Json.Create(("sdk", Sdk), ("sourceSha256", Json.Sha(source)), ("probeSha256", Json.Sha(Path.Combine(Sample, "probe/Program.cs"))), ("projectSha256", Json.Sha(Path.Combine(Sample, "probe/AmountProbe.csproj"))), ("manifestSha256", Json.Sha(manifest)), ("harnessSha256", Json.Sha(Tool)), ("cliSha256", Json.Sha(Advice)));
            var bound = SampleInvestigations.BuildFiles.Where(n => n != "Directory.Packages.props").Append("eng/Assay.Tooling/DecisionExperiments.cs").ToDictionary(n => n, n => Json.Sha(Path.Combine(Root, n)), StringComparer.Ordinal); identities["buildInputSha256"] = Json.Node(bound);
            foreach (var (key, value) in identities)
            {
                report[key] = value?.DeepClone();
            }

            report["reviewStatus"] = corpus["reviewStatus"]?.DeepClone(); report["requirements"] = corpus["requirements"]?.DeepClone();
            using var scratch = new TemporaryWorkspace("assay-decision-"); var originalTarget = Path.Combine(scratch.Path, "original"); var originalBuild = await Compile(originalTarget, original); Json.Check(originalBuild.S("status") == "compiled", "Original fixture did not compile."); report["originalBuild"] = originalBuild.DeepClone();
            foreach (var item in cases)
            {
                var receipt = await ExecuteCase(item!, corpus["suites"]!, originalTarget, scratch.Path, original);
                foreach (var (key, value) in identities)
                {
                    receipt[key] = value?.DeepClone();
                }

                receipt["schema"] = "playground-mutation-experiment/0.1"; receipt["synthetic"] = true; receipt["caseId"] = item.S("id"); receipt["selectedSuite"] = item.S("selectedSuite"); receipt["review"] = item!["review"]?.DeepClone(); receipt["reviewStatus"] = corpus["reviewStatus"]?.DeepClone(); receipt["authority"] = report.S("authority");
                Json.Check(receipt.S("outcome") == item.S("expectedOutcome"), "Observed mutation outcome differs from declared expectation.");
                var folder = Path.Combine(Output, "cases", item.S("id")); var packet = Path.Combine(folder, "experiment.json"); Json.Write(packet, receipt); var advice = await SampleInvestigations.AdviceFor(packet); Json.Write(Path.Combine(folder, "advice.json"), advice);
                Json.Check(advice.S("action") == item.S("expectedAction") && advice.S("adviceStatus") == item.S("expectedStatus"), "Advice differs from declared review.");
                report.A("results").Add(Json.Create(("id", item.S("id")), ("outcome", receipt.S("outcome")), ("action", advice.S("action")), ("adviceStatus", advice.S("adviceStatus")), ("verificationVerdict", advice.S("verificationVerdict")), ("repairDemonstrated", receipt.B("repairDemonstrated")), ("survivorDisposition", receipt.S("survivorDisposition")), ("review", item["review"]), ("expectationsMatched", true), ("experimentSha256", Json.Sha(packet)), ("adviceSha256", Json.Sha(Path.Combine(folder, "advice.json"))), ("experiment", Relative(Output, packet))));
                if (item.S("id") == "zero-boundary-gap")
                {
                    Json.Write(Path.Combine(Output, "experiment.json"), receipt); Json.Write(Path.Combine(Output, "advice.json"), advice);
                    var firm = await SampleInvestigations.AdviceFor(packet, "--firm"); Json.Check(firm.S("action") == advice.S("action") && firm.S("message").StartsWith("Attention:", StringComparison.Ordinal), "Tone changed investigation."); Json.Write(Path.Combine(Output, "firm-advice.json"), firm);
                    var unavailableRun = await Processes.Run(["dotnet", Advice, packet, "--jev"], Root, Path.Combine(folder, "missing-provider.log"), 40, new Dictionary<string, string> { ["TYPESAFE_API_KEY"] = "" }); unavailableRun.Require("Missing provider handling");
                    var unavailable = Json.Parse(unavailableRun.Output); Json.Check(unavailable.S("adviceStatus") == "unavailable" && unavailable.S("deterministicAction") == advice.S("action"), "Missing provider hid deterministic advice."); Json.Write(Path.Combine(Output, "unavailable-advice.json"), unavailable);
                }
                Json.Write(path, report);
            }
            foreach (var (name, hash) in bound)
            {
                Json.Check(Json.Sha(Path.Combine(Root, name)) == hash, "Bound execution input changed.");
            }

            Json.Check(Json.Sha(source) == identities.S("sourceSha256") && Json.Sha(Advice) == identities.S("cliSha256") && Json.Sha(Tool) == identities.S("harnessSha256"), "Bound source/tool changed.");
            var summary = new JsonObject(); foreach (var outcome in new[] { "survived", "killed", "invalid", "baseline_failed" })
            {
                summary[outcome] = report.A("results").Count(r => r.S("outcome") == outcome);
            }

            report["summary"] = summary; report["executionStatus"] = "completed"; Json.Write(path, report); return report;
        }
        catch (Exception failure) { report["executionStatus"] = "failed"; report["failure"] = failure.Message; Json.Write(path, report); throw; }
    }

    [GeneratedRegex("\\berror (\\w+):")]
    private static partial Regex MyRegex { get; }
}
