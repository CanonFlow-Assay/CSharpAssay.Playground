namespace Assay.Tooling;

// Three fixed tutorial exercises; no arbitrary mutation, patch or agent execution API.
public static class ChangeVerificationExercise
{
    private const string Sample = "samples/100-expenseguard";
    private const string Solution = "ExpenseGuard.slnx";
    private const string TestInput = "tests/ExpenseGuard.Tests/ExpenseGuard.Tests.csproj";
    private const string PolicySource = "src/ExpenseGuard.Domain/PolicyEngine.cs";
    private const string TestSource = "tests/ExpenseGuard.Tests/PolicyTests.cs";
    private const string SpecimenProject = "src/ExpenseGuard.ChangeExercise/ExpenseGuard.ChangeExercise.csproj";
    private static readonly string[] SpecimenNamespaces = ["ExpenseGuard.ChangeExercise"];
    private static readonly string[] SpecimenProjects = [SpecimenProject];
    private const string Specimen = "src/ExpenseGuard.ChangeExercise/Counterexample.cs";

    public static async Task<JsonObject> Run(string output)
    {
        output = Path.GetFullPath(output);
        Json.Check(output.StartsWith(Path.Combine(Root, "evidence/generated/expenseguard/"), StringComparison.Ordinal) && !Directory.Exists(output),
            "Choose a fresh ignored ExpenseGuard evidence directory; failed attempts are retained.");
        Json.Check((await Processes.Git(Root, "status", "--porcelain")).Length == 0, "Commit exercise inputs before qualification.");
        Json.Check(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASSAY_INVENTORY_DATABASE")), "Real PostgreSQL is required; no skip/fake fallback.");
        var source = await Processes.Git(Root, "rev-parse", "HEAD");
        var packedTool = PackageTools.PackedTool;
        var toolHash = Json.Sha(packedTool);
        var packageHash = Json.Sha(Path.Combine(Root, ".packages/CsAssay.Tool.0.2.0-rc.1.nupkg"));
        var sample = Path.Combine(Root, Sample);
        var declared = (await Processes.Git(Root, "ls-files", "-z", "--", Sample)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Json.Check(declared.Length > 0, "ExpenseGuard tracked input inventory is empty.");
        var original = Snapshot(sample);
        Directory.CreateDirectory(output);
        var report = Json.Create(("schema", "expenseguard-change-verification/0.1"), ("executionStatus", "running"),
            ("sourceCommit", source), ("sampleSnapshotSha256", Json.Digest(original)), ("installedToolSha256", toolHash),
            ("toolPackageSha256", packageHash), ("providerRequests", 0), ("realPayments", 0), ("runs", new JsonArray()),
            ("scope", "Configured admitted checks and 189 required application tests; no complete correctness or release approval."));
        var reportPath = Path.Combine(output, "exercises.json");
        Json.Write(reportPath, report);
        try
        {
            foreach (var exercise in new[] { "A", "B", "C" })
            {
                var fixture = Path.Combine(output, "fixtures", exercise);
                foreach (var file in declared)
                {
                    var input = Inside(Root, file);
                    Json.Check(File.Exists(input) && new FileInfo(input).LinkTarget is null, "Tracked exercise input must be a regular file.");
                    var target = Inside(fixture, Relative(sample, input));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(input, target);
                }
                File.Copy(Path.Combine(Root, "Directory.Build.targets"), Path.Combine(fixture, "Directory.Build.targets"));
                if (exercise == "A") AddCoreSpecimen(fixture);
                if (exercise == "C") AddEvidenceSpecimen(fixture);
                var policyHash = Json.Sha(Path.Combine(fixture, ".csassay.json"));
                var testsBefore = Snapshot(Path.Combine(fixture, "tests"));
                var baselineInputs = Snapshot(fixture);
                var baseline = await Verify(exercise, "baseline", fixture);
                Complete(baseline);
                if (exercise == "C") Complete(await Verify("C-zero", "baseline", fixture, true));

                var changedPath = Path.Combine(fixture, exercise switch { "A" => Specimen, "B" => PolicySource, _ => TestSource });
                var good = File.ReadAllText(changedPath);
                var bad = exercise switch
                {
                    "A" => ReplaceOnce(good, "get; init;", "get; set;"),
                    "B" => ReplaceOnce(good, "amount > policy.ReceiptThreshold", "amount >= policy.ReceiptThreshold"),
                    _ => ReplaceOnce(good, "[Fact] public void Missing_fx_never_approves()", "[Fact(Skip = \"Change verification: required evidence deliberately absent\")] public void Missing_fx_never_approves()")
                };
                File.WriteAllText(changedPath, bad);
                var fault = await Verify(exercise, "fault", fixture);
                var e = fault["evidence"]!;
                Json.Check(e.A("failures").Count == 0, "A deliberate exercise became a tool/setup failure: " + exercise);
                if (exercise == "A")
                {
                    Json.Check(fault.S("verdict") == "fail" && fault.I("exitCode") == 1 && e.B("authoritative") &&
                        e.A("missingEvidence").Count == 0 && e.A("tests").All(t => t.S("outcome") == "passed" && t.I("passed") == 189),
                        "A requires a completed admitted blocker with all required tests passed.");
                    var finding = e.A("findings").Single(f => f.S("ruleId") == "CSAI0001" && f.S("scope") == "core" && !f.B("suppressed"))!;
                    Json.Check(finding.S("disposition") == "block" && finding["location"]!.S("path").EndsWith("Counterexample.cs", StringComparison.Ordinal) &&
                        finding["location"]!.I("startLine") > 0 && finding.S("message").Length > 0, "A lacks the admitted rule/location/explanation.");
                    var explanation = await Processes.Run(["dotnet", packedTool, "explain", "CSAI0001"], fixture, Path.Combine(output, "A-explain.log"));
                    explanation.Require("Packaged rule explanation");
                    Json.Check(explanation.Output.Contains("Status: Admitted", StringComparison.Ordinal) && explanation.Output.Contains("docs/rules/CSAI0001.md", StringComparison.Ordinal), "A lacks admitted documentation.");
                }
                else if (exercise == "B")
                {
                    Json.Check(fault.S("verdict") == "fail" && fault.I("exitCode") == 1 && e.B("authoritative") &&
                        e.A("missingEvidence").Count == 0 && SameObservations(baseline, fault) &&
                        e.A("tests").Any(t => t.S("outcome") == "failed" && t.I("total") == 189 && t.I("failed") > 0 &&
                            t.A("failureDetails").Any(d => d.S("testName").Contains("Exact_thresholds", StringComparison.Ordinal) &&
                                d.S("message").Contains("Approve", StringComparison.Ordinal) && d.S("message").Contains("MoreInfo", StringComparison.Ordinal))),
                        "B must fail required business tests without inventing an analyzer finding.");
                }
                else
                {
                    Json.Check(fault.S("verdict") == "inconclusive" && fault.I("exitCode") == 2 && !e.B("authoritative") &&
                        SameObservations(baseline, fault) && e.A("missingEvidence").Any(m => m.S("code") == "CSASSAY-REQUIRED-TESTS-NOT-RUN") &&
                        e.A("tests").Any(t => t.S("input") == TestInput && t.S("outcome") == "notRun" &&
                            t.I("total") == 189 && t.I("passed") == 188 && t.I("failed") == 0 && t.I("skipped") == 1),
                        "C must retain its required input and report the skipped evidence as incomplete.");
                }
                if (exercise == "C")
                {
                    var zero = await Verify("C-zero", "fault", fixture, true);
                    Json.Check(zero.S("verdict") == "inconclusive" && zero.I("exitCode") == 2 && !zero["evidence"]!.B("authoritative") &&
                        zero["evidence"]!.A("findings").Count == 0 && zero["evidence"]!.A("failures").Count == 0 &&
                        zero["evidence"]!.A("missingEvidence").Any(m => m.S("code") == "CSASSAY-REQUIRED-TESTS-NOT-RUN"),
                        "Zero findings in the separate evidence-only core fixture must not authorize absent required tests.");
                }
                Json.Check(Json.Sha(Path.Combine(fixture, ".csassay.json")) == policyHash, "Exercise changed policy/grants/requirements.");
                if (exercise != "C") Json.Check(JsonNode.DeepEquals(testsBefore, Snapshot(Path.Combine(fixture, "tests"))), "Repair must not alter application tests/assertions.");
                File.WriteAllText(changedPath, good);
                Json.Check(JsonNode.DeepEquals(baselineInputs, Snapshot(fixture)), "Source repair changed unrelated fixture inputs.");
                if (exercise == "C") Complete(await Verify("C-zero", "repaired", fixture, true));
                Complete(await Verify(exercise, "repaired", fixture));
                Json.Check(JsonNode.DeepEquals(testsBefore, Snapshot(Path.Combine(fixture, "tests"))), "Original application tests were not restored.");
            }
            Json.Check(source == await Processes.Git(Root, "rev-parse", "HEAD") && (await Processes.Git(Root, "status", "--porcelain")).Length == 0 &&
                JsonNode.DeepEquals(original, Snapshot(sample)) && PackageTools.PackedTool == packedTool && Json.Sha(packedTool) == toolHash,
                "Source, package or tooling changed during qualification.");
            report["executionStatus"] = "completed";
            Json.Write(reportPath, report); return report;
        }
        catch (Exception error)
        {
            report["executionStatus"] = "failed"; report["failure"] = error.Message;
            Json.Write(reportPath, report); throw;
        }

        async Task<JsonNode> Verify(string exercise, string stage, string fixture, bool evidenceOnly = false)
        {
            var runDirectory = Path.Combine(output, "runs", exercise + "-" + stage); Directory.CreateDirectory(runDirectory);
            var inputs = Snapshot(fixture); Json.Write(Path.Combine(runDirectory, "inputs.json"), inputs);
            var commands = new JsonArray();
            async Task<Execution> Execute(string[] command, string label)
            {
                commands.Add(Json.Create(("workingDirectory", fixture), ("arguments", command)));
                Json.Write(Path.Combine(runDirectory, "commands.json"), commands);
                return await Processes.Run(command, fixture, Path.Combine(runDirectory, label + ".log"), 360);
            }
            Console.WriteLine("Change verification: " + exercise + " " + stage);
            (await Execute(["dotnet", "restore", evidenceOnly ? SpecimenProject : Solution, "--locked-mode", "--configfile", Path.Combine(Root, "NuGet.config")], "restore")).Require("Exercise locked restore");
            // The A specimen uses normal C# compilation without an IDE analyzer package.
            // Existing Domain/package build enforcement remains enabled and unchanged.
            (await Execute(["dotnet", "build", evidenceOnly ? SpecimenProject : Solution, "--no-restore", "-c", "Release", "-m:2", "-nodeReuse:false"], "build")).Require("Compiling exercise specimen");
            var execution = await Execute(["dotnet", packedTool, "verify", Path.Combine(fixture, evidenceOnly ? SpecimenProject : Solution), "--policy", Path.Combine(fixture, evidenceOnly ? "zero-findings.csassay.json" : ".csassay.json"),
                "--json", Path.Combine(runDirectory, "verify.json"), "--sarif", Path.Combine(runDirectory, "verify.sarif"),
                "--html", Path.Combine(runDirectory, "verify.html"), "--markdown", Path.Combine(runDirectory, "verify.md")], "verify");
            var receipt = Json.Read(Path.Combine(runDirectory, "verify.json"));
            if (evidenceOnly) Json.Check(receipt["evidence"]!.A("findings").Count == 0, "The supplementary evidence fixture must have literally zero findings.");
            Json.Check(execution.State is not (ProcessState.TimedOut or ProcessState.Cancelled) && execution.ExitCode == receipt.I("exitCode"), "Verifier exit/report disagreement.");
            Json.Check(JsonNode.DeepEquals(inputs, Snapshot(fixture)), "Build/verification changed declared source inputs.");
            report.A("runs").Add(Json.Create(("exercise", exercise), ("stage", stage), ("verdict", receipt.S("verdict")), ("exitCode", execution.ExitCode),
                ("inputSnapshotSha256", Json.Digest(inputs)), ("report", Relative(output, Path.Combine(runDirectory, "verify.json"))),
                ("reportSha256", Json.Sha(Path.Combine(runDirectory, "verify.json")))));
            Json.Write(reportPath, report); return receipt;
        }
    }

    private static void Complete(JsonNode receipt)
    {
        var e = receipt["evidence"]!;
        Json.Check(receipt.S("verdict") == "pass" && receipt.I("exitCode") == 0 && e.B("authoritative") && e.S("verificationMode") == "verification" &&
            e.A("missingEvidence").Count == 0 && e.A("failures").Count == 0 &&
            e.A("tests").Count == 1 && e.A("tests").All(t => t.S("input") == TestInput && t.B("required") && t.S("outcome") == "passed" &&
                t.I("passed") == 189 && t.I("failed") == 0 && t.I("skipped") == 0), "Baseline/repair lacks fresh full configured verification.");
    }

    private static bool SameObservations(JsonNode baseline, JsonNode fault) =>
        JsonNode.DeepEquals(baseline["evidence"]!["findings"], fault["evidence"]!["findings"]);

    private static void WriteSpecimen(string fixture)
    {
        var project = Path.Combine(fixture, SpecimenProject); Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json"),
            "{\"version\":1,\"dependencies\":{\"net11.0\":{}}}\n");
        File.WriteAllText(Path.Combine(fixture, Specimen), "namespace ExpenseGuard.ChangeExercise;\npublic sealed record Counterexample { public int Value { get; init; } }\n");
    }

    private static void AddEvidenceSpecimen(string fixture)
    {
        WriteSpecimen(fixture);
        // Additional narrowly declared evidence fixture, not a replacement for
        // C's full application verification. All original test obligations remain.
        var policy = Json.Read(Path.Combine(fixture, ".csassay.json"));
        policy["boundaries"]!["coreProjects"] = Json.Array(SpecimenProjects);
        policy["boundaries"]!["coreNamespaces"] = Json.Array(SpecimenNamespaces);
        Json.Write(Path.Combine(fixture, "zero-findings.csassay.json"), policy);
    }

    private static void AddCoreSpecimen(string fixture)
    {
        WriteSpecimen(fixture);
        var solution = XDocument.Load(Path.Combine(fixture, Solution)); solution.Root!.Add(new XElement("Project", new XAttribute("Path", SpecimenProject)));
        solution.Save(Path.Combine(fixture, Solution));
        var policyPath = Path.Combine(fixture, ".csassay.json"); var policy = Json.Read(policyPath);
        policy["boundaries"]!.A("coreProjects").Add(SpecimenProject); policy["boundaries"]!.A("coreNamespaces").Add("ExpenseGuard.ChangeExercise");
        Json.Write(policyPath, policy);
    }

    private static JsonObject Snapshot(string directory) => Json.Create(("files", Json.Array(Files(directory).Order(StringComparer.Ordinal)
        .Select(p => Json.Create(("path", Relative(directory, p)), ("sha256", Json.Sha(p)))))));

    private static string ReplaceOnce(string source, string before, string after)
    {
        Json.Check(source.Split(before, StringSplitOptions.None).Length == 2, "Exercise anchor changed; review the fixed specimen.");
        return source.Replace(before, after, StringComparison.Ordinal);
    }
}
