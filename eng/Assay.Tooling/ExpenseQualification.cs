using Assay.Workflow.Domain;
using Playground.DecisionAssist.Domain;

namespace Assay.Tooling;

public static class ExpenseQualification
{
    private const string Sample = "samples/100-expenseguard";
    private const string Solution = "ExpenseGuard.slnx";
    private static readonly string[] Shared = ["global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.config", "AllSamples.slnx", ".editorconfig"];
    private static readonly string[] Remaining = ["Independent human business/policy review", "Real identity provider and tenant isolation", "Receipt authenticity/malware scanning and storage operations", "Live model/extraction quality and privacy qualification", "Real payment provider and reconciliation", "Operational/security/load/platform/browser acceptance"];
    private static readonly JsonSerializerOptions ProtocolOptions = new(Json.Options) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false) } };

    public static async Task<JsonObject> Run(string output)
    {
        output = Path.GetFullPath(output);
        Json.Check(output.StartsWith(Path.Combine(Root, "evidence/generated/expenseguard/"), StringComparison.Ordinal) && !Directory.Exists(output), "Choose a fresh ExpenseGuard evidence directory.");
        Json.Check((await Processes.Git(Root, "status", "--porcelain")).Length == 0, "Commit ExpenseGuard before source-bound qualification.");
        Json.Check(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASSAY_INVENTORY_DATABASE")), "Real PostgreSQL execution required.");
        var sample = Path.Combine(Root, Sample);
        var source = await Processes.Git(Root, "rev-parse", "HEAD");
        var declared = (await Processes.Git(Root, "ls-files", "-z", "--", Sample)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Json.Check(declared.Length > 0 && declared.All(p => !p.Split('/').Any(s => s is ".local" or ".tools" or ".secrets" or ".env" or "login.json")),
            "Private runtime data cannot become declared source evidence.");
        var sourceFiles = declared.Select(p => Inside(Root, p)).ToArray();
        Json.Check(sourceFiles.All(p => File.Exists(p) && new FileInfo(p).LinkTarget is null), "Declared source must be regular tracked files.");
        var bindings = Bindings(sourceFiles);
        var snapshot = Json.Digest(bindings);
        var engine = Json.Sha(Engine);
        var tool = Json.Sha(Tool);
        var reportPath = Path.Combine(output, "qualification.json");
        var report = Json.Create(("schema", "expenseguard-qualification/0.1"), ("executionStatus", "running"),
            ("sourceCommit", source), ("snapshotSha256", snapshot), ("inputSha256", bindings), ("engineSha256", engine),
            ("qualifierSha256", tool), ("providerRequests", 0), ("realPayments", 0), ("mutationResults", new JsonArray()),
            ("structuralControls", new JsonArray()), ("authority", "Bounded Linux/PostgreSQL/HTTP application exercise. Selected CSAssay rules and executed tests; not universal security, correctness or deployment approval."));
        Json.Write(reportPath, report);
        var manifest = Json.Create(("testDll", "tests/ExpenseGuard.Tests/bin/Release/net11.0/ExpenseGuard.Tests.dll"),
            ("minimumTests", 159), ("testReportProtocol", "xunit-mtp-v1"));
        async Task Build(string directory, string name)
        {
            (await Processes.Run(["dotnet", "restore", Path.Combine(directory, Solution), "--locked-mode", "--configfile", Path.Combine(Root, "NuGet.config")], directory, Path.Combine(output, name + "-restore.log"), 180)).Require(name + " locked restore");
            (await Processes.Run(["dotnet", "build", Path.Combine(directory, Solution), "--no-restore", "-c", "Release", "-m:2", "-nodeReuse:false"], directory, Path.Combine(output, name + "-build.log"), 180)).Require(name + " build");
        }
        try
        {
            Console.WriteLine("ExpenseGuard: building frozen baseline and executing real HTTP/PostgreSQL tests.");
            await Build(sample, "baseline");
            var tests = await ApplicationRunner.Tests(sample, manifest, Path.Combine(output, "baseline"));
            Json.Write(Path.Combine(output, "baseline.json"), tests);
            Json.Check(tests.S("state") == "Passed", "ExpenseGuard baseline failed; keep the test report and logs.");
            report["behavior"] = tests.DeepClone(); Json.Write(reportPath, report);
            var total = tests.I("total"); var names = tests.Strings("testNames");
            Console.WriteLine($"ExpenseGuard: {total} baseline tests passed. Running authoritative CSAssay verify.");
            var verify = await Processes.Run(["dotnet", Engine, "verify", Path.Combine(sample, Solution), "--policy", Path.Combine(sample, ".csassay.json"),
                "--json", Path.Combine(output, "verify.json"), "--sarif", Path.Combine(output, "verify.sarif")], Root, Path.Combine(output, "verify.log"), 300);
            verify.Require("ExpenseGuard authoritative verification");
            var receipt = Json.Read(Path.Combine(output, "verify.json")); var evidence = receipt["evidence"]!;
            Json.Check(receipt.S("verdict") == "pass" && evidence.B("authoritative") && evidence.S("verificationMode") == "verification" &&
                evidence.A("missingEvidence").Count == 0 && evidence.A("failures").Count == 0 && evidence.A("tests").Sum(t => t.I("passed")) == total &&
                evidence.A("tests").All(t => t.I("failed") == 0 && t.I("skipped") == 0), "Incomplete ExpenseGuard verify receipt.");
            report["csassayVerdict"] = "Pass";
            foreach (var item in evidence.A("sources").Where(s => s.S("path").StartsWith("src/", StringComparison.Ordinal) || s.S("path").StartsWith("tests/", StringComparison.Ordinal)))
            {
                if (!item.S("path").Split('/').Any(p => p is "bin" or "obj"))
                    Json.Check(bindings[Sample + "/" + item.S("path")]?.GetValue<string>() == item.S("sha256"), "An authored compiler input differs from the declared snapshot.");
            }
            var assess = await Processes.Run(["dotnet", Engine, "assess", Path.Combine(sample, Solution), "--policy", Path.Combine(sample, ".csassay.json"),
                "--json", Path.Combine(output, "assessment.json")], Root, Path.Combine(output, "assess.log"), 300);
            assess.Require("ExpenseGuard assessment API");
            var assessmentReceipt = Json.Read(Path.Combine(output, "assessment.json"));
            Json.Check(assessmentReceipt["receipt"].S("verdict") == "pass" && assessmentReceipt["receipt"]!["evidence"].B("authoritative"), "Assessment API lost verification authority.");

            var corpus = Json.Read(Path.Combine(sample, "mutations.json"));
            Json.Check(corpus.A("cases").Count is >= 1 and <= 12 && corpus.A("cases").Select(c => c.S("id")).Distinct(StringComparer.Ordinal).Count() == corpus.A("cases").Count,
                "Mutation corpus exceeds its finite budget or contains duplicate identities.");
            foreach (var mutation in corpus.A("cases"))
            {
                var id = mutation.S("id"); Json.Check(Regex.IsMatch(id, "^[a-z0-9-]{1,64}$"), "Invalid mutation ID.");
                Console.WriteLine("ExpenseGuard mutation: " + id);
                var fixture = Path.Combine(output, "fixtures", id); CopyFixture(sample, fixture, sourceFiles);
                var path = Inside(fixture, mutation.S("path")); var text = File.ReadAllText(path);
                Json.Check(text.Split(mutation.S("before"), StringSplitOptions.None).Length == 2, "Mutation anchor is not unique: " + id);
                File.WriteAllText(path, text.Replace(mutation.S("before"), mutation.S("after"), StringComparison.Ordinal));
                await Build(fixture, id);
                var testOutput = Path.Combine(output, "mutations", id);
                var observed = await ApplicationRunner.Tests(fixture, manifest, testOutput, total, expectedNames: names);
                Json.Write(Path.Combine(testOutput, "tests.json"), observed);
                Json.Check(observed.S("state") is "Passed" or "Failed", "Mutation execution is invalid/incomplete, not a kill: " + id);
                var state = observed.S("state") == "Failed" ? "Killed" : "Survived";
                Json.Check(state != "Killed" || observed.Strings("failedTests").Any(n => n.StartsWith(mutation.S("test"), StringComparison.Ordinal)), "Mutation failed without its declared requirement assertion: " + id);
                report.A("mutationResults").Add(Json.Create(("id", id), ("requirement", mutation.S("requirement")), ("state", state),
                    ("expected", mutation.S("expected")), ("failed", observed.I("failed")), ("reportSha256", observed.S("reportSha256"))));
                Json.Write(reportPath, report);
                if (id == "approve-without-receipt")
                {
                    var rejected = await Processes.Run(["dotnet", Engine, "verify", Path.Combine(fixture, Solution), "--policy", Path.Combine(fixture, ".csassay.json"),
                        "--json", Path.Combine(output, "missing-receipt-verify.json"), "--sarif", Path.Combine(output, "missing-receipt-verify.sarif")], fixture, Path.Combine(output, "missing-receipt-verify.log"), 300);
                    var failedReceipt = Json.Read(Path.Combine(output, "missing-receipt-verify.json"));
                    Json.Check(rejected.ExitCode == 1 && failedReceipt.S("verdict") == "fail" && failedReceipt["evidence"]!.A("tests").Any(t => t.I("failed") > 0) &&
                        failedReceipt["evidence"]!.A("missingEvidence").Count == 0 && failedReceipt["evidence"]!.A("failures").Count == 0,
                        "Missing-receipt shortcut must produce a completed required-test failure, not a tool failure.");
                    report["missingReceiptVerify"] = Json.Create(("verdict", "Fail"), ("exitCode", 1), ("detector", "Required business tests, carried by CSAssay verification"));
                }
            }
            foreach (var id in new[] { "mutable-core", "unhandled-union-state" })
            {
                var fixture = Path.Combine(output, "fixtures", id); CopyFixture(sample, fixture, sourceFiles);
                var path = Path.Combine(fixture, "src/ExpenseGuard.Domain/Model.cs");
                if (id == "mutable-core") File.AppendAllText(path, "\npublic sealed record MutableCounterexample { public int Value { get; set; } }\n");
                else
                {
                    var text = File.ReadAllText(path); const string anchor = ", AuditHeld => \"AuditHeld\"";
                    Json.Check(text.Split(anchor, StringSplitOptions.None).Length == 2, "Exhaustiveness control anchor changed.");
                    File.WriteAllText(path, text.Replace(anchor, "", StringComparison.Ordinal));
                }
                (await Processes.Run(["dotnet", "restore", Path.Combine(fixture, Solution), "--locked-mode", "--configfile", Path.Combine(Root, "NuGet.config")], fixture, Path.Combine(output, id + "-restore.log"), 180)).Require(id + " restore");
                var execution = await Processes.Run(["dotnet", "build", "src/ExpenseGuard.Domain/ExpenseGuard.Domain.csproj", "--no-restore", "-c", "Release"], fixture, Path.Combine(output, id + ".log"), 180);
                var code = id == "mutable-core" ? "CSAI0001" : "CS8509";
                Json.Check(execution.ExitCode == 1 && execution.Output.Contains(code, StringComparison.Ordinal), "Structural control did not fail as declared: " + id);
                report.A("structuralControls").Add(Json.Create(("id", id), ("detector", id == "mutable-core" ? "packaged CSAssay analyzer" : "C# compiler"), ("code", code), ("exitCode", execution.ExitCode)));
                Json.Write(reportPath, report);
            }

            var requirementHash = Json.Sha(Path.Combine(sample, "docs/requirements.md"));
            Json.Write(Path.Combine(output, "mutations.json"), Json.Create(("schema", "expenseguard-mutation-results/0.1"),
                ("sourceCommit", source), ("snapshotSha256", snapshot), ("results", report["mutationResults"]), ("structuralControls", report["structuralControls"])));
            ModuleEvidence Module(string name, string file, bool authoritative) => new(name, ExecutionState.Passed, true, authoritative,
                snapshot, requirementHash, [new(Relative(output, file), Json.Sha(file))], []);
            var survived = report.A("mutationResults").Any(r => r.S("state") == "Survived");
            var assessment = new Assessment(AssessmentRules.Schema, snapshot, requirementHash, SupportStatus.Native, ExecutionState.Passed,
                [new("expense-policy", "Execute the declared expense requirements and preserve authority boundaries.", [])],
                [Module("CSharpAssay", Path.Combine(output, "verify.json"), true), Module("Behavior", Path.Combine(output, "baseline.json"), false),
                    new("MutationAssay", survived ? ExecutionState.Survived : ExecutionState.Passed, false, false, snapshot, requirementHash,
                        [new("mutations.json", Json.Sha(Path.Combine(output, "mutations.json")))], ["Agent-authored finite mutation set; independent adjudication pending."])]);
            // The DAA API consumes the actual observed modules. It cannot issue a release approval.
            var decision = ApplicationAssistance.Decide(assessment, [], 3);
            var choice = decision switch
            {
                NoInvestigation => "NoInvestigation", SurvivorReview => "ReviewSurvivor", ContextRequired => "RequestContext",
                ProbeRequested => "RunRegisteredProbe", BudgetExhausted => "BudgetExhausted", null => "RequestContext"
            };
            Json.Write(Path.Combine(output, "daa-assessment.json"), JsonSerializer.SerializeToNode(assessment, ProtocolOptions)!);
            report["decisionAssist"] = Json.Create(("workflowStatus", AssessmentRules.Evaluate(assessment).ToString()), ("decision", choice),
                ("authority", "Deterministic development assistance; no model call or business approval authority."));
            Json.Check(report.A("mutationResults").All(r => r.S("state") == r.S("expected")), "Unexpected mutation outcomes remain open.");
            Json.Check((await Processes.Git(Root, "rev-parse", "HEAD")) == source && (await Processes.Git(Root, "status", "--porcelain")).Length == 0 &&
                JsonNode.DeepEquals(bindings, Bindings(sourceFiles)) && Json.Sha(Engine) == engine && Json.Sha(Tool) == tool, "Source or tooling changed during qualification.");
            Json.Write(Path.Combine(output, "build-evidence.json"), Json.Create(("domainAssemblySha256", Json.Sha(Path.Combine(sample, "src/ExpenseGuard.Domain/bin/Release/net11.0/ExpenseGuard.Domain.dll"))),
                ("reportSha256", Json.Sha(Path.Combine(output, "verify.json"))), ("sourceCommit", source), ("snapshotSha256", snapshot)));
            report["sourceUnchanged"] = true; report["executionStatus"] = "completed";
            report["remaining"] = Json.Array(Remaining);
            Json.Write(reportPath, report);
            return report;
        }
        catch (Exception error)
        { report["executionStatus"] = "failed"; report["failure"] = error.Message; Json.Write(reportPath, report); throw; }
    }
    private static JsonObject Bindings(string[] sourceFiles)
    {
        var result = new JsonObject();
        foreach (var path in sourceFiles.Order(StringComparer.Ordinal))
            result[Relative(Root, path)] = Json.Sha(path);
        foreach (var name in Shared) result[name] = Json.Sha(Path.Combine(Root, name));
        return result;
    }
    private static void CopyFixture(string sample, string destination, string[] sourceFiles)
    {
        foreach (var path in sourceFiles)
        {
            var target = Path.Combine(destination, Relative(sample, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target);
        }
        File.Copy(Path.Combine(Root, "Directory.Build.targets"), Path.Combine(destination, "Directory.Build.targets"));
    }
}
