namespace Assay.Tooling;

public static class EfBoundaryQualification
{
    private const string RelativeSample = "samples/90-ef-boundaries";
    private static readonly string[] RootInputs = ["global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.config", ".editorconfig", "tests/Directory.Build.props"];

    public static async Task<JsonObject> Run(string output)
    {
        output = Path.GetFullPath(output);
        Json.Check(output.StartsWith(Path.Combine(Root, "evidence/generated/"), StringComparison.Ordinal) && !Directory.Exists(output), "Use a fresh EF evidence directory.");
        Json.Check((await Processes.Git(Root, "status", "--porcelain")).Length == 0, "EF qualification requires a clean committed source.");
        var source = await Processes.Git(Root, "rev-parse", "HEAD");
        var engineHash = Json.Sha(Engine);
        var toolingHash = Json.Sha(Tool);
        var original = Bindings(Path.Combine(Root, RelativeSample));
        var report = Json.Create(("schema", "assay-ef-boundary/0.1"), ("executionStatus", "running"), ("sourceCommit", source),
            ("engineSha256", engineHash), ("toolSha256", toolingHash), ("inputSha256", original), ("provider", "Microsoft.EntityFrameworkCore.Sqlite 11.0.0-rc.2.26504.105; in-memory SQLite connection"),
            ("providerRequests", 0), ("cases", new JsonArray()), ("authority", "Bounded persistence, policy-boundary and consumer checks. No broad EF, other provider, NativeAOT, trimming, IDE, Windows or production qualification."));
        var reportPath = Path.Combine(output, "qualification.json");
        Json.Write(reportPath, report);
        var cache = new Dictionary<string, string> { ["NUGET_PACKAGES"] = Path.Combine(Root, ".packages/cache") };
        async Task<JsonNode> ExecuteCase(string name, string root, string command, int expected)
        {
            var directory = Path.Combine(output, name);
            Directory.CreateDirectory(directory);
            var solution = Path.Combine(root, RelativeSample, "EfBoundary.slnx");
            var receiptPath = Path.Combine(directory, command + ".json");
            var execution = await Processes.Run(["dotnet", Engine, command, solution, "--json", receiptPath, "--sarif", Path.Combine(directory, command + ".sarif")], root, Path.Combine(directory, command + ".log"), 600, cache);
            Json.Check(execution.ExitCode == expected && File.Exists(receiptPath), "Unexpected EF case execution: " + name + "; retain the raw log.");
            var receipt = Json.Read(receiptPath);
            var evidence = receipt["evidence"]!;
            Json.Check(receipt.I("exitCode") == expected && evidence.A("failures").Count == 0 && evidence.A("missingEvidence").Count == 0, "EF case has unexpected execution gaps: " + name);
            if (command == "verify")
            {
                Json.Check(evidence.A("tests").Sum(test => test.I("total")) >= 8 && evidence.A("tests").All(test => test.S("outcome") is "passed" or "failed"), "EF required tests did not complete.");
            }
            report.A("cases").Add(Json.Create(("name", name), ("command", command), ("exitCode", expected), ("verdict", receipt.S("verdict")),
                ("authoritative", evidence.B("authoritative")), ("receipt", Relative(output, receiptPath)), ("receiptSha256", Json.Sha(receiptPath)), ("inputSha256", Bindings(Path.Combine(root, RelativeSample)))));
            Json.Write(reportPath, report);
            return receipt;
        }
        async Task Restore(string root, string name)
        {
            var execution = await Processes.Run(["dotnet", "restore", Path.Combine(root, RelativeSample, "EfBoundary.slnx"), "--locked-mode", "--configfile", Path.Combine(root, "NuGet.config")], root, Path.Combine(output, name + "-restore.log"), 300, cache);
            execution.Require("EF locked restore " + name);
        }
        try
        {
            await Restore(Root, "baseline");
            var baseline = await ExecuteCase("baseline", Root, "verify", 0);
            Json.Check(baseline["evidence"].B("authoritative") && baseline["evidence"]!.A("findings").Any(finding => finding.S("ruleId") == "CSAN0004" && finding.S("scope") == "shell" && finding.S("disposition") == "advise" && !finding.B("suppressed")), "Nullable persistence transport advice must remain visible.");
            foreach (var name in new[] { "core-blocking", "valid-grant", "expired-grant", "mismatched-grant", "mapping-failure", "persistence-failure" })
            {
                var root = Path.Combine(output, "fixtures", name);
                CopyFixture(root);
                var sample = Path.Combine(root, RelativeSample);
                if (name is "mapping-failure" or "persistence-failure")
                {
                    var path = Path.Combine(sample, "src/EfBoundary.Infrastructure/OrderStore.cs");
                    var text = File.ReadAllText(path);
                    var old = name == "mapping-failure" ? "row.Id, row.Quantity, memo" : "await transaction.CommitAsync(cancellationToken)";
                    var replacement = name == "mapping-failure" ? "row.Id, row.Quantity + 1, memo" : "await transaction.RollbackAsync(cancellationToken)";
                    Json.Check(text.Split(old, StringSplitOptions.None).Length == 2, "EF mutation anchor must be unique.");
                    File.WriteAllText(path, text.Replace(old, replacement, StringComparison.Ordinal));
                    await Restore(root, name);
                    var failed = await ExecuteCase(name, root, "verify", 1);
                    Json.Check(failed["evidence"]!.A("tests").Any(test => test.I("failed") > 0) && !failed["evidence"]!.A("findings").Any(finding => finding.S("disposition") == "block" && !finding.B("suppressed")), "Shell mapping/persistence failure must remain a completed test failure.");
                    continue;
                }
                var domainPath = Path.Combine(sample, "src/EfBoundary.Domain/OrderValues.cs");
                File.AppendAllText(domainPath, "\npublic sealed record CoreCounterexample { public int Value { get; set; } }\n");
                await Restore(root, name);
                var discovered = await ExecuteCase(name + "-identity", root, "check", 1);
                var finding = discovered["evidence"]!.A("findings").Single(finding => finding.S("ruleId") == "CSAI0001");
                Json.Check(finding.S("scope") == "core" && finding.S("disposition") == "block", "Core counterexample was demoted.");
                if (name == "core-blocking") { continue; }
                var fingerprint = finding.S("fingerprint");
                var date = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
                var expiry = name == "expired-grant" ? date.AddDays(-1) : date.AddDays(30);
                if (name == "mismatched-grant") { fingerprint = (fingerprint[0] == '0' ? "1" : "0") + fingerprint[1..]; }
                var policyPath = Path.Combine(sample, ".csassay.json");
                var policy = Json.Read(policyPath).AsObject();
                policy["suppressions"] = JsonSerializer.SerializeToNode(new[] { new { ruleId = "CSAI0001", owner = "ef-sample-maintainer", reason = "Scoped generated counterexample grant for the boundary demonstration; no authenticated human approval", expires = expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), fingerprint } });
                Json.Write(policyPath, policy);
                var result = await ExecuteCase(name, root, name == "valid-grant" ? "verify" : "check", name == "valid-grant" ? 0 : 1);
                if (name == "valid-grant")
                {
                    Json.Check(result["evidence"]!.A("suppressions").Any(grant => grant.B("authorized") && grant.S("fingerprint") == fingerprint), "Real fingerprinted grant evidence is missing.");
                    File.Copy(policyPath, Path.Combine(output, "documented-real-grant.json"));
                }
                else
                {
                    Json.Check(result["evidence"]!.A("findings").Any(item => item.S("ruleId") == "CSAI0001" && !item.B("suppressed")), "Invalid grant hid the core finding.");
                }
            }
            Json.Check((await Processes.Git(Root, "rev-parse", "HEAD")) == source && (await Processes.Git(Root, "status", "--porcelain")).Length == 0 &&
                Json.Sha(Engine) == engineHash && Json.Sha(Tool) == toolingHash && JsonNode.DeepEquals(original, Bindings(Path.Combine(Root, RelativeSample))), "EF source or tooling changed during qualification.");
            report["executionStatus"] = "completed";
            report["sourceUnchanged"] = true;
            report["remaining"] = Json.Array(["Independent human review", "Other providers and deployment operations", "IDE and Windows hosts", "NativeAOT and trimming"]);
            Json.Write(reportPath, report);
            return report;
        }
        catch (Exception error) { report["executionStatus"] = "failed"; report["failure"] = error.Message; Json.Write(reportPath, report); throw; }
    }

    private static JsonObject Bindings(string directory)
    {
        var result = new JsonObject();
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(path => !Relative(directory, path).Split('/').Any(part => part is "bin" or "obj" or "TestResults")).Order(StringComparer.Ordinal))
        {
            result[Relative(directory, path)] = Json.Sha(path);
        }
        return result;
    }

    private static void CopyFixture(string destination)
    {
        foreach (var relative in RootInputs.Concat(Directory.EnumerateFiles(Path.Combine(Root, RelativeSample), "*", SearchOption.AllDirectories)
            .Select(path => Relative(Root, path)).Where(path => !path.Split('/').Any(part => part is "bin" or "obj" or "TestResults"))))
        {
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(Root, relative), target);
        }
        var package = "CsAssay.Analyzers.0.2.0-rc.1.nupkg";
        Directory.CreateDirectory(Path.Combine(destination, ".packages"));
        File.Copy(Path.Combine(Root, ".packages", package), Path.Combine(destination, ".packages", package));
    }
}
