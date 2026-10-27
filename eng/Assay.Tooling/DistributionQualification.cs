namespace Assay.Tooling;

public static class DistributionQualification
{
    private static readonly string[] Admitted = ["CSAI0001", "CSAI0002", "CSAN0001", "CSAN0002", "CSAN0003", "CSAN0004", "CSAP0001"];
    private static readonly string[] Frameworks = ["net11.0"];
    private static readonly string[] CoreProjects = ["Example.csproj"];
    private const string NativeProject = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><LangVersion>preview</LangVersion><Nullable>enable</Nullable><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup></Project>\n";

    public static async Task WriteProject(string folder, string code)
    {
        Directory.CreateDirectory(folder);
        // Standalone controlled specimens: compile examples before applying the verifier's policy.
        File.WriteAllText(Path.Combine(folder, "Example.csproj"), NativeProject);
        File.WriteAllText(Path.Combine(folder, "Directory.Build.props"), "<Project />\n");
        File.WriteAllText(Path.Combine(folder, "Directory.Build.targets"), "<Project />\n");
        File.Copy(Path.Combine(Root, "global.json"), Path.Combine(folder, "global.json"), true);
        File.WriteAllText(Path.Combine(folder, "Guide.cs"), code);
        Json.Write(Path.Combine(folder, ".csassay.json"), Json.Create(("profile", "native"),
            ("release", Json.Create(("allowPreviewToolchain", true), ("requiredTargetFrameworks", Frameworks), ("requiredRules", Admitted), ("tests", new JsonArray()))),
            ("boundaries", Json.Create(("coreProjects", CoreProjects), ("shellProjects", Array.Empty<string>()), ("coreNamespaces", Array.Empty<string>()), ("shellNamespaces", Array.Empty<string>()))),
            ("representations", Json.Create(("resultTypes", Array.Empty<string>()), ("optionTypes", Array.Empty<string>()), ("closedTypes", Array.Empty<string>()))), ("domainPrimitives", new JsonObject()), ("suppressions", new JsonArray())));
        (await Processes.Run(["dotnet", "restore", "Example.csproj", "--configfile", Path.Combine(Root, "NuGet.config")], folder, Path.Combine(folder, "restore.log"))).Require("Controlled specimen restore");
    }

    public static async Task<JsonObject> Run(string engineRoot, string expenseEvidence, string output)
    {
        engineRoot = Path.GetFullPath(engineRoot); expenseEvidence = Path.GetFullPath(expenseEvidence); output = Path.GetFullPath(output);
        Json.Check(output.StartsWith(Path.Combine(Root, "evidence/generated/distribution/"), StringComparison.Ordinal) && !Directory.Exists(output), "Choose a fresh ignored distribution output.");
        var source = await Processes.Git(Root, "rev-parse", "HEAD");
        var sdk = await Processes.Run(["dotnet", "--version"], Root); sdk.Require("Inspect exact SDK"); Json.Check(sdk.Output.Trim() == Sdk, "Exact SDK selection differs from the pin.");
        Json.Check((await Processes.Git(Root, "status", "--porcelain")).Length == 0, "Commit distribution source before execution.");
        var engineQualification = Json.Read(Path.Combine(engineRoot, "artifacts/csharp-tooling-qualified/qualification.json"));
        Json.Check(engineQualification.S("sourceCommit") == await Processes.Git(engineRoot, "rev-parse", "HEAD") && engineQualification.B("releaseQualified") &&
            (await Processes.Git(engineRoot, "status", "--porcelain")).Length == 0, "Use the exact preserved qualified engine.");
        var expense = Json.Read(Path.Combine(expenseEvidence, "qualification.json"));
        Json.Check(expense.S("executionStatus") == "completed" && expense.B("sourceUnchanged") && expense.S("schema") == "expenseguard-qualification/0.1" &&
            await Processes.Git(Root, "merge-base", expense.S("sourceCommit"), source) == expense.S("sourceCommit"), "Complete ancestor ExpenseGuard qualification is required.");
        // Tooling-only commits can reuse unchanged application bytes, never relabel the old source.
        var applicationInputs = expense["inputSha256"]!.AsObject();
        Json.Check(Json.Digest(applicationInputs) == expense.S("snapshotSha256"), "Application source binding is malformed.");
        var currentApplication = (await Processes.Git(Root, "ls-files", "-z", "--", "samples/100-expenseguard")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Json.Check(currentApplication.ToHashSet(StringComparer.Ordinal).SetEquals(applicationInputs.Select(p => p.Key).Where(p => p.StartsWith("samples/100-expenseguard/", StringComparison.Ordinal))), "Declared application inventory changed; requalify it.");
        foreach (var (path, hash) in applicationInputs) Json.Check(Json.Sha(Inside(Root, path)) == hash!.GetValue<string>(), "Declared application input changed; requalify: " + path);
        Directory.CreateDirectory(output);
        var report = Json.Create(("schema", "csassay-distribution-qualification/0.1"), ("executionStatus", "running"), ("sourceCommit", source),
            ("engineSourceCommit", engineQualification.S("sourceCommit")), ("engineQualificationSha256", Json.Sha(Path.Combine(engineRoot, "artifacts/csharp-tooling-qualified/qualification.json"))),
            ("expenseQualificationSourceCommit", expense.S("sourceCommit")), ("expenseQualificationSha256", Json.Sha(Path.Combine(expenseEvidence, "qualification.json"))), ("toolSha256", Json.Sha(Tool)),
            ("providerRequests", 0), ("realPayments", 0), ("entries", new JsonArray()));
        Json.Write(Path.Combine(output, "qualification.json"), report);
        try
        {
            var provenance = Json.Create(("producerCommit", source), ("engineSourceCommit", engineQualification.S("sourceCommit")),
                ("applicationQualificationSourceCommit", expense.S("sourceCommit")),
                ("sdk", Sdk), ("analyzerPackageSha256", Json.Sha(Path.Combine(Root, ".packages/CsAssay.Analyzers.0.2.0-rc.1.nupkg"))),
                ("toolPackageSha256", Json.Sha(Path.Combine(Root, ".packages/CsAssay.Tool.0.2.0-rc.1.nupkg"))));
            JsonObject? repair = null;
            foreach (var id in Admitted)
            {
                var guide = File.ReadAllText(Path.Combine(engineRoot, "docs/rules", id + ".md")).Replace("\r\n", "\n", StringComparison.Ordinal);
                var snippets = guide.Split("```csharp", StringSplitOptions.None).Skip(1).Select(p => p[..p.IndexOf("```", StringComparison.Ordinal)].Trim()).ToArray();
                Json.Check(snippets.Length == 2, "Canonical guide must have two executable examples: " + id);
                if (id == "CSAI0001") repair = Json.Create(("before", snippets[0]), ("after", snippets[1]));
                for (var n = 0; n < snippets.Length; n++)
                {
                    var name = id + (n == 0 ? "-before" : "-after"); var folder = Path.Combine(output, "fixtures", name);
                    await WriteProject(folder, snippets[n]);
                    (await Processes.Run(["dotnet", "build", "Example.csproj", "--no-restore", "-c", "Release"], folder, Path.Combine(folder, "compile.log"))).Require("Guide compiles: " + name);
                    var path = Path.Combine(folder, "check.json"); var sarif = Path.Combine(folder, "check.sarif");
                    var check = await Processes.Run(["dotnet", PackageTools.PackedTool, "check", Path.Combine(folder, "Example.csproj"), "--policy", Path.Combine(folder, ".csassay.json"), "--json", path, "--sarif", sarif], Root, Path.Combine(folder, "check.log"));
                    var receipt = Json.Read(path); var found = receipt["evidence"]!.A("findings").Any(f => f.S("ruleId") == id);
                    var missing = receipt["evidence"]!.A("missingEvidence");
                    // The canonical disabled-flow example intentionally removes nullable evidence.
                    // Record that exact known gap; no other missing input or execution failure is accepted.
                    var disabledFlow = id == "CSAN0001" && n == 0;
                    var declaredGap = disabledFlow ? missing.Count == 1 && missing[0].S("code") == "CSASSAY-NULLABLE-FLOW-INCOMPLETE" : missing.Count == 0;
                    Json.Check(check.ExitCode is 0 or 1 && found == (n == 0) && receipt["evidence"]!.A("failures").Count == 0 && declaredGap, "Guide observation disagrees with named rule: " + name);
                    var payload = Json.Create(("kind", "rule"), ("title", guide.Split('\n')[0].TrimStart('#', ' ')), ("ruleId", id), ("ruleStatus", "Admitted"),
                        ("boundary", "Declared core project Example.csproj"), ("status", "Recorded example"), ("variant", n == 0 ? "Before" : "After"),
                        ("code", snippets[n]), ("codeSha256", Json.Hash(snippets[n])), ("guide", guide),
                        ("expected", n == 0 ? id + " observed" : id + " absent"),
                        ("expectedMissingEvidence", disabledFlow ? "CSASSAY-NULLABLE-FLOW-INCOMPLETE: deliberately disabled compiler evidence" : "None"),
                        ("execution", Json.Create(("state", "Completed"), ("compileExitCode", 0), ("checkExitCode", check.ExitCode),
                            ("command", "dotnet <byte-verified local package>/cs-assay.dll check Example.csproj --policy .csassay.json --json check.json"), ("compileLogSha256", Json.Sha(Path.Combine(folder, "compile.log"))))),
                        ("documentation", "https://github.com/CanonFlow-Assay/CSharpAssay/blob/717d5d2daff548b580c651c7bc1247964bc826c2/docs/rules/" + id + ".md"),
                        ("observation", DistributionReports.PublicReceipt(receipt, Json.Sha(path), Json.Read(sarif))), ("provenance", provenance));
                    report.A("entries").Add(DistributionReports.Fixture(name, payload)); Json.Write(Path.Combine(output, "qualification.json"), report);
                }
            }
            await NativeCases(output, report, provenance);
            report["repair"] = await RepairExercise.Run(Root, Path.Combine(output, "repair"), repair!);
            var observations = Path.Combine(output, "runtime"); Directory.CreateDirectory(observations);
            var manifest = Json.Create(("testDll", "tests/ExpenseGuard.Tests/bin/Release/net11.0/ExpenseGuard.Tests.dll"), ("minimumTests", 5), ("testReportProtocol", "xunit-mtp-v1"));
            var previous = Environment.GetEnvironmentVariable("ASSAY_GUIDED_OBSERVATIONS");
            try
            {
                Environment.SetEnvironmentVariable("ASSAY_GUIDED_OBSERVATIONS", observations);
                var tests = await ApplicationRunner.Tests(Path.Combine(Root, "samples/100-expenseguard"), manifest, Path.Combine(output, "runtime-tests"), selectedClass: "ExpenseGuard.Tests.GuidedDemoTests");
                Json.Check(tests.S("state") == "Passed", "Guided HTTP/DB tests failed."); report["runtimeTests"] = tests;
            }
            finally { Environment.SetEnvironmentVariable("ASSAY_GUIDED_OBSERVATIONS", previous); }
            foreach (var path in Directory.GetFiles(observations, "D*.json").Order(StringComparer.Ordinal))
            {
                var payload = Json.Read(path).AsObject(); payload["provenance"] = provenance.DeepClone();
                if (payload.S("id") == "D01") payload["observation"]!["pairedAdviceControl"] = Json.Read(Path.Combine(observations, "D01-advice-invariance.json"))["observation"]!.DeepClone();
                report.A("entries").Add(DistributionReports.Fixture(payload.S("id"), payload));
            }
            var api = Path.Combine(Root, "samples/100-expenseguard/src/ExpenseGuard.Api/bin/Release/net11.0/ExpenseGuard.Api.dll");
            (await Processes.Run(["dotnet", api, "--guided-freshness", expenseEvidence, Path.Combine(output, "freshness")], Root, Path.Combine(output, "freshness.log"))).Require("Actual bound freshness control");
            var freshness = Json.Read(Path.Combine(output, "freshness/observation.json")).AsObject(); freshness["provenance"] = provenance.DeepClone();
            report.A("entries").Add(DistributionReports.Fixture("D06", freshness));
            // D04 links the actual packaged structural finding; D05 links the separate compiler and prototype observations.
            foreach (var (id, title, linked) in new[] { ("D04", "A public record setter", "CSAI0001-before"), ("D05", "Compiler and prototype have different jobs", "CSAU0004-catch-all") })
            {
                var linkedObservation = Json.Parse(report.A("entries").Single(e => e.S("id") == linked).S("payload"))["observation"];
                report.A("entries").Add(DistributionReports.Fixture(id, Json.Create(("kind", "scenario"), ("title", title), ("status", "Recorded example"),
                    ("expected", id == "D04" ? "CSAI0001 structural finding" : "Compiler rejects missing case; catch-all compiles with prototype advice"),
                    ("observation", Json.Create(("linkedFixture", linked), ("recordedEvidence", linkedObservation), ("compilerControl", id == "D05" ? Json.Parse(report.A("entries").Single(e => e.S("id") == "compiler-missing-case").S("payload"))["observation"] : null), ("detector", id == "D04" ? "Packaged CSAssay" : "C# compiler and separate CSAU0004 prototype"))),
                    ("limits", "Recorded isolated specimen. No browser button edits application source; follow local reproduction instructions."), ("provenance", provenance))));
            }
            var verifyPath = Path.Combine(expenseEvidence, "verify.json"); var verify = Json.Read(verifyPath);
            var binding = Json.Create(("sourceCommit", expense.S("sourceCommit")), ("sourceUnchanged", true), ("reportSha256", Json.Sha(verifyPath)), ("observedAt", DateTimeOffset.UtcNow.ToString("O")));
            var policy = Json.Read(Path.Combine(Root, "samples/100-expenseguard/.csassay.json"));
            binding["scope"] = string.Join(", ", policy["boundaries"]!.Strings("coreProjects").Select(p => Path.GetFileNameWithoutExtension(p)));
            var summary = DistributionReports.BoundSummary(verify, binding, expense.S("sourceCommit"), Json.Sha(verifyPath), "expenseguard-receipt.json");
            summary["fixtureProducerCommit"] = source;
            Json.Write(Path.Combine(output, "expenseguard-receipt.json"), DistributionReports.PublicReceipt(verify, Json.Sha(verifyPath), Json.Read(Path.Combine(expenseEvidence, "verify.sarif"))));
            Json.Write(Path.Combine(output, "evidence-index.json"), summary);
            DistributionReports.WriteBadge(Path.Combine(output, "status.svg"), summary);
            foreach (var entry in report.A("entries")) _ = DistributionReports.ValidateFixture(entry!);
            Json.Check(report.A("entries").Select(e => e.S("id")).Distinct(StringComparer.Ordinal).Count() == report.A("entries").Count &&
                Enumerable.Range(1, 6).All(n => report.A("entries").Any(e => e.S("id") == "D0" + n)), "Six guided cases missing or duplicate.");
            Json.Check(source == await Processes.Git(Root, "rev-parse", "HEAD") && (await Processes.Git(Root, "status", "--porcelain")).Length == 0 && Json.Sha(Tool) == report.S("toolSha256"), "Source/tool changed during distribution qualification.");
            report["executionStatus"] = "completed"; report["sourceUnchanged"] = true;
            Json.Write(Path.Combine(output, "examples.json"), Json.Create(("schema", "csassay-recorded-examples/0.1"), ("entries", report["entries"])));
            Json.Write(Path.Combine(output, "qualification.json"), report); return report;
        }
        catch (Exception error) { report["executionStatus"] = "failed"; report["failure"] = error.Message; Json.Write(Path.Combine(output, "qualification.json"), report); throw; }
    }

    public static async Task<JsonObject> Stage(string output)
    {
        output = Path.GetFullPath(output); Json.Check(output.StartsWith(Path.Combine(Root, "evidence/generated/distribution/"), StringComparison.Ordinal), "Select retained local distribution evidence.");
        var report = Json.Read(Path.Combine(output, "qualification.json"));
        Json.Check(report.S("executionStatus") == "completed" && report.B("sourceUnchanged") && report.S("sourceCommit") == await Processes.Git(Root, "rev-parse", "HEAD") &&
            (await Processes.Git(Root, "status", "--porcelain")).Length == 0 && report.S("toolSha256") == Json.Sha(Tool), "Stage only exact clean completed producer evidence.");
        var pack = Json.Read(Path.Combine(output, "examples.json")); foreach (var entry in pack.A("entries")) _ = DistributionReports.ValidateFixture(entry!);
        Json.Write(Path.Combine(Root, "docs/examples/catalogue.json"), pack);
        Json.Write(Path.Combine(Root, "samples/100-expenseguard/src/ExpenseGuard.Api/wwwroot/guide-examples.json"), pack);
        foreach (var (from, to) in new[] { ("expenseguard-receipt.json", "recorded-expenseguard-receipt.json"), ("evidence-index.json", "recorded-evidence-index.json"), ("status.svg", "recorded-status.svg") })
            File.Copy(Path.Combine(output, from), Path.Combine(Root, "docs/examples", to), true);
        var indexPath = Path.Combine(Root, "docs/examples/recorded-evidence-index.json"); var index = Json.Read(indexPath); index["receipt"] = "recorded-expenseguard-receipt.json";
        index["kind"] = "Historical recorded example at the producer SHA; not current-main status"; Json.Write(indexPath, index);
        var claimsPath = Path.Combine(Root, "docs/distribution/claims.json"); var claims = Json.Read(claimsPath); claims["producerCommit"] = report.S("sourceCommit");
        claims["recordingQualificationSha256"] = Json.Sha(Path.Combine(output, "qualification.json"));
        foreach (var claim in claims.A("claims")) claim!["status"] = "executed";
        foreach (var law in claims.A("laws").Where(l => l.S("id") != "L06")) law!["status"] = "executed controls; independent human review pending";
        Json.Write(claimsPath, claims);
        return Json.Create(("executionStatus", "completed"), ("producerCommit", report.S("sourceCommit")), ("stagedEntries", pack.A("entries").Count),
            ("warning", "Asset changes invalidate prior current-application qualification. Commit and requalify before review; historical examples retain their producer identities."));
    }

    public static JsonObject Pack()
    {
        var files = new[] { Path.Combine(Root, "docs/distribution"), Path.Combine(Root, "docs/examples"), Path.Combine(Root, "docs/assets"), Path.Combine(Root, "samples/100-expenseguard/src/ExpenseGuard.Api/wwwroot") }.SelectMany(Files)
            .Append(Path.Combine(Root, "docs/index.html"))
            .Where(p => Path.GetFileName(p) != "pack-manifest.json").Order(StringComparer.Ordinal)
            .Select(p => Json.Create(("path", Relative(Root, p)), ("sha256", Json.Sha(p)), ("approvalState", "DRAFT")));
        var result = Json.Create(("schema", "csassay-publication-pack/0.1"), ("approvalState", "DRAFT"), ("targetAccount", "Not selected"),
            ("externalPublicationAuthorized", false), ("files", Json.Array(files)), ("limits", "No publisher or approval bypass. Human approval must identify channel/account and exact content/asset bytes; edits require new approval."));
        result["contentDigest"] = Json.Digest(result["files"]!); Json.Write(Path.Combine(Root, "docs/distribution/pack-manifest.json"), result); return result;
    }

    private static async Task NativeCases(string output, JsonObject report, JsonObject provenance)
    {
        const string prefix = "public sealed record Yes; public sealed record No; public readonly union Choice(Yes, No); public static class Decisions { public static string Show(Choice c) => c switch { Yes => \"yes\"";
        foreach (var catchAll in new[] { false, true })
        {
            var id = catchAll ? "CSAU0004-catch-all" : "compiler-missing-case"; var code = prefix + (catchAll ? ", _ => \"fallback\"" : "") + " }; }\n";
            var folder = Path.Combine(output, "fixtures", id); await WriteProject(folder, code);
            var compile = await Processes.Run(["dotnet", "build", "Example.csproj", "--no-restore", "-c", "Release", "-p:TreatWarningsAsErrors=true"], folder, Path.Combine(folder, "compile.log"));
            JsonNode observation;
            if (!catchAll)
            {
                Json.Check(compile.ExitCode == 1 && compile.Output.Contains("CS8509", StringComparison.Ordinal), "Compiler control did not report CS8509.");
                observation = Json.Create(("headline", "Compiler rejected missing case"), ("detector", "C# compiler CS8509"), ("exitCode", 1), ("authoritative", false));
            }
            else
            {
                compile.Require("Catch-all must compile"); var path = Path.Combine(folder, "check.json"); var sarif = Path.Combine(folder, "check.sarif");
                var check = await Processes.Run(["dotnet", PackageTools.PackedTool, "check", Path.Combine(folder, "Example.csproj"), "--policy", Path.Combine(folder, ".csassay.json"), "--json", path, "--sarif", sarif], Root, Path.Combine(folder, "check.log"));
                var receipt = Json.Read(path); Json.Check(check.ExitCode == 0 && receipt["evidence"]!.A("findings").Any(f => f.S("ruleId") == "CSAU0004"), "Prototype catch-all control changed.");
                observation = DistributionReports.PublicReceipt(receipt, Json.Sha(path), Json.Read(sarif));
            }
            report.A("entries").Add(DistributionReports.Fixture(id, Json.Create(("kind", "rule"), ("title", "Compiler versus prototype"), ("ruleId", catchAll ? "CSAU0004" : "CS8509"),
                ("ruleStatus", catchAll ? "Prototype — advisory, nonblocking" : "Compiler diagnostic"), ("boundary", "Isolated native-union specimen"), ("status", "Recorded example"),
                ("code", code), ("codeSha256", Json.Hash(code)), ("guide", "A catch-all can compile while hiding future cases. Native exhaustiveness belongs to the compiler. CSAU0004 remains a prototype; it is not admitted."),
                ("observation", observation), ("provenance", provenance))));
        }
    }
}
