namespace Assay.Tooling;

public static class MigrationQualification
{
    private static readonly string[] Remaining = ["Independent case/advisory adjudication", "Explicit held-out/live-provider evaluation", "Windows host and production operational qualification"];
    public static async Task<JsonObject> Run(string output, string? engineCheckout = null)
    {
        output = Path.GetFullPath(output); Json.Check(output.StartsWith(Path.Combine(Root, "evidence/generated/").Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal), "Keep migration evidence under ignored evidence/generated/.");
        var path = Path.Combine(output, "qualification.json"); Json.Check(!File.Exists(path), "Choose a fresh qualification directory; prior attempts are retained.");
        var engineRoot = Path.GetFullPath(engineCheckout ?? Path.Combine(Root, "../CSharpAssay"));
        Json.Check(Engine == Path.Combine(engineRoot, "src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll"), "Selected ASSAY_RUNNER must belong to the declared engine checkout.");
        var source = await Processes.Git(Root, "rev-parse", "HEAD"); var engineSource = await Processes.Git(engineRoot, "rev-parse", "HEAD");
        Json.Check((await Processes.Git(Root, "status", "--porcelain")).Length == 0 && (await Processes.Git(engineRoot, "status", "--porcelain")).Length == 0, "Qualification requires exact clean source commits.");
        var enginePath = Path.Combine(engineRoot, "artifacts/csharp-tooling-qualified/qualification.json"); var engine = Json.Read(enginePath);
        Json.Check(engine.S("sourceCommit") == engineSource && engine.S("executionStatus") == "completed" && engine.B("releaseQualified") && engine.B("sourceUnchanged") && engine.A("gates").Count == 7 && engine.A("gates").All(g => g.S("state") == "Passed"), "Exact paired engine release qualification is required.");
        var report = Json.Create(("schema", "assay-csharp-migration-qualification/0.1"), ("executionStatus", "running"), ("sourceCommit", source), ("engineSourceCommit", engineSource), ("engineQualificationSha256", Json.Sha(enginePath)), ("toolSha256", Json.Sha(Tool)), ("sdkSource", Json.Read(Path.Combine(Root, ".packages/sdk-source.json"))), ("gates", new JsonArray()), ("providerRequests", 0), ("publication", "local Forgejo only"), ("authority", "Source-bound local qualification. No automatic human adjudication, provider quality or production claim.")); Json.Write(path, report);
        try
        {
            var application = Path.Combine(output, "application"); var inventory = Path.Combine(output, "inventory");
            (string Name, string[] Command)[] gates = [
                ("no-interpreter-dependency", ["dotnet", Tool, "self-audit"]),
                ("all-authored-samples", ["./eng/run-all-samples.sh"]),
                ("primary-evidence", ["./eng/run-assay.sh", Engine]),
                ("gof-evidence", ["./eng/run-gof-crosswalk.sh"]),
                ("shape-evidence", ["./eng/run-functional-shape.sh"]),
                ("decision-mutations", ["dotnet", Tool, "run-decision-assistance"]),
                ("decision-scenarios", ["dotnet", Tool, "evaluate-decision-assistance"]),
                ("shape-investigations", ["dotnet", Tool, "run-shape-investigations"]),
                ("csv-evidence", ["./eng/run-csvhelper.sh", Engine]),
                ("new-application-requirement-loop", ["dotnet", Tool, "qualify-application-workflow", "--output", application]),
                ("inventory-requirement-and-contract-loop", ["dotnet", Tool, "with-inventory-database", "--", "dotnet", Tool, "qualify-inventory-maturity", "--output", inventory]),
                ("hefty-physical-workloads", ["dotnet", Tool, "run-csvhelper-hefty", "--reuse-fixtures"]),
                ("physical-roundtrip", ["dotnet", Tool, "run-csvhelper-roundtrip"]),
                ("frozen-milestone-comparison", ["dotnet", Tool, "compare-csvhelper-milestones"]),
                ("ef-boundary-contract", ["dotnet", Tool, "qualify-ef-boundary", "--output", Path.Combine(output, "ef-boundary")])];
            foreach (var (name, command) in gates)
            {
                Console.WriteLine("C# migration gate: " + name); var log = Path.Combine(output, name + ".log"); var watch = Stopwatch.StartNew(); var execution = await Processes.Run(command, Root, log, 7200);
                report.A("gates").Add(Json.Create(("name", name), ("state", execution.StateName), ("exitCode", execution.ExitCode), ("seconds", Math.Round(watch.Elapsed.TotalSeconds, 3)), ("log", Path.GetFileName(log)), ("logSha256", Json.Sha(log)))); Json.Write(path, report); execution.Require(name);
                Json.Check(await Processes.Git(Root, "rev-parse", "HEAD") == source && (await Processes.Git(Root, "status", "--porcelain")).Length == 0 && Json.Sha(Tool) == report.S("toolSha256"), "Source or tooling changed during qualification.");
            }
            Json.Check(await Processes.Git(engineRoot, "rev-parse", "HEAD") == engineSource && (await Processes.Git(engineRoot, "status", "--porcelain")).Length == 0 && Json.Sha(enginePath) == report.S("engineQualificationSha256"), "Engine source/qualification changed.");
            var identities = new JsonObject();
            foreach (var file in new[] { "evidence/generated/csharp-migration/self-audit.json", "evidence/generated/all-samples/toolchain.json", "evidence/generated/refined/verify.json", "evidence/generated/csvhelper/validation.json", "evidence/generated/decision-assistance/mutation-cases.json", "evidence/generated/decision-assistance/evaluation-none.json", "evidence/generated/shape-orders/investigations.json", "evidence/generated/csvhelper/hefty/comparison.json", "evidence/generated/csvhelper/roundtrip/comparison.json" })
            {
                identities[file] = Json.Sha(Path.Combine(Root, file));
            }
            foreach (var directory in new[] { application, inventory })
            {
                identities[Relative(Root, Path.Combine(directory, "qualification.json"))] = Json.Sha(Path.Combine(directory, "qualification.json"));
            }
            report["evidenceSha256"] = identities; report["executionStatus"] = "completed"; report["sourceUnchanged"] = true; report["remaining"] = Json.Array(Remaining); Json.Write(path, report); return report;
        }
        catch (Exception error) { report["executionStatus"] = "failed"; report["failure"] = error.Message; Json.Write(path, report); throw; }
    }
}
