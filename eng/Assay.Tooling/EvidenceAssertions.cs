namespace Assay.Tooling;

public static class EvidenceAssertions
{
    private static readonly string[] Required = ["CSAI0001", "CSAI0002", "CSAN0001", "CSAN0002", "CSAN0003", "CSAN0004", "CSAP0001"];
    private static readonly string[] Catalogue = ["CSAA0001", "CSAA0002", "CSAD0001", "CSAD0002", "CSAE0001", "CSAE0002", "CSAF0001", "CSAF0002", "CSAI0001", "CSAI0002", "CSAI0003", "CSAN0001", "CSAN0002", "CSAN0003", "CSAN0004", "CSAP0001", "CSAU0001", "CSAU0002", "CSAU0004"];
    private static readonly string[] sourceArray = new[] { "src", "tests" };

    public static void Native(JsonNode evidence)
    {
        Json.Check(evidence.S("toolVersion") == "0.2.0-rc.1" && evidence.S("requestedProfile") == "native" && evidence.S("profile") == "native", "Evidence must use the pinned native engine/profile.");
        foreach (var project in evidence.A("projects"))
        {
            Json.Check((project.S("targetFramework") == "net11.0" || project.S("targetFramework").StartsWith("net11.0-", StringComparison.Ordinal)) && project.S("profile") == "native" && project.S("profileEvidence") == "native" && project.S("languageVersion") == "preview" && project.B("loaded"), "Incomplete native project evidence.");
        }
    }
    private static JsonNode ValidReport(string path, string verdict, bool authoritative, int projectCount)
    {
        var report = Json.Read(path); var evidence = report["evidence"]!; Native(evidence);
        Json.Check(report.S("verdict") == verdict && report.I("exitCode") == 0 && evidence.B("authoritative") == authoritative && evidence.A("projects").Count == projectCount && evidence.A("failures").Count == 0 && evidence.A("missingEvidence").Count == 0, "Receipt verdict/authority/completeness changed: " + path);
        return report;
    }
    public static void AssertPrimaryReceipt(JsonNode report, JsonNode expected)
    {
        var evidence = report["evidence"]!; Native(evidence);
        Json.Check(report.S("verdict") == expected.S("verdict") && report.I("exitCode") == expected.I("exitCode") && evidence["authoritative"]?.GetValue<bool>() == expected.B("authoritative") && evidence.S("verificationMode") == expected.S("verificationMode") && evidence.A("projects").Count == expected.I("projects"), "Receipt verdict/authority/mode changed.");
        Json.Check(evidence["failures"] is JsonArray failures && failures.Count == 0, "Execution failures cannot satisfy a primary fixture.");
        var missing = evidence["missingEvidence"] as JsonArray ?? throw new InvalidDataException("Missing compiler-evidence inventory.");
        var counts = new JsonObject(); foreach (var group in missing.GroupBy(item => item.S("code"), StringComparer.Ordinal)) { counts[group.Key] = group.Count(); }
        Json.Check(JsonNode.DeepEquals(counts, expected["missingEvidenceCounts"] ?? new JsonObject()), "Unexpected compiler-evidence gap inventory.");
        if (missing.Count > 0)
        {
            Json.Check(report.S("verdict") == "fail" && report.I("exitCode") == 1 && !evidence.B("authoritative") && evidence.S("verificationMode") == "analysis" && evidence.A("findings").Any(finding => finding.S("ruleId") == "CSAP0001" && finding.S("disposition") == "block" && finding?["suppressed"]?.GetValue<bool>() == false), "A declared nullable negative must retain active integrity failure and no release authority.");
        }
    }
    private static JsonObject Counts(IEnumerable<JsonNode?> findings)
    {
        var result = new JsonObject(); foreach (var group in findings.GroupBy(f => f.S("ruleId"), StringComparer.Ordinal))
        {
            result[group.Key] = group.Count();
        }

        return result;
    }
    public static void Primary()
    {
        var expectations = Json.Read(Path.Combine(Root, "evidence/expectations/reports.json"));
        foreach (var expectedEntry in expectations.A("reports"))
        {
            var expected = expectedEntry ?? throw new InvalidDataException("Missing primary report expectation.");
            var report = Json.Read(Path.Combine(Root, expected.S("path"))); AssertPrimaryReceipt(report, expected); var evidence = report["evidence"]!;
            var sarif = Json.Read(Path.ChangeExtension(Path.Combine(Root, expected.S("path")), ".sarif"));
            Json.Check(sarif.S("version") == "2.1.0" && sarif.A("runs").Count == 1 && sarif.A("runs")[0].A("invocations").Count == 1, "Primary SARIF invocation metadata missing.");
            var invocation = sarif.A("runs")[0].A("invocations")[0];
            Json.Check(invocation.I("exitCode") == expected.I("exitCode") && invocation?["executionSuccessful"]?.GetValue<bool>() == (evidence.A("missingEvidence").Count == 0), "Primary SARIF execution/completeness mapping changed.");
            var counts = expected!["ruleCounts"]!.DeepClone().AsObject(); foreach (var (key, value) in expected["advisoryRuleCounts"]?.AsObject() ?? [])
            {
                counts[key] = value?.DeepClone();
            }

            foreach (var key in counts.Where(p => p.Value!.GetValue<int>() == 0).Select(p => p.Key).ToArray())
            {
                _ = counts.Remove(key);
            }

            Json.Check(JsonNode.DeepEquals(Counts(evidence.A("findings")), counts), "Unexpected finding inventory.");
            foreach (var field in new[] { "ruleCounts", "advisoryRuleCounts" })
            {
                foreach (var (id, count) in expected[field]?.AsObject() ?? [])
                {
                    var rule = evidence.A("rules").Single(r => r.S("id") == id); var required = field == "ruleCounts";
                    Json.Check(rule.B("required") == required && rule.S("outcome") == "completed" && rule.I("findingCount") == count!.GetValue<int>(), "Rule authority/outcome/count changed: " + id);
                    if (!required)
                    {
                        Json.Check(evidence.A("findings").Where(f => f.S("ruleId") == id).All(f => f.S("disposition") == "advise" && !f.B("suppressed")), "Advisory suppressed or promoted.");
                    }
                }
            }

            Json.Check(evidence.A("tests").Sum(t => t.I("passed")) == expected.I("testsPassed") && evidence.A("tests").All(t => t.S("outcome") == "passed" && t.I("failed") == 0), "Configured test gate changed or failed.");
        }
        var sample = Path.Combine(Root, "samples/10-gilded-rose"); var provenance = Json.Read(Path.Combine(sample, "provenance.json"));
        foreach (var entry in provenance["import"]!.A("files").Concat(provenance.A("supportingMaterial")))
        {
            Json.Check(Json.Sha(Inside(sample, entry.S("localPath"))) == entry.S("localSha256"), "Pinned provenance changed: " + entry.S("localPath"));
        }
    }
    public static void Gof()
    {
        var sample = Path.Combine(Root, "samples/30-gof-functional-crosswalk"); var expected = Json.Read(Path.Combine(sample, "expected-findings.json"));
        PackageBaseline(expected, sample, ["classic", "refined"]);
        var readme = File.ReadAllText(Path.Combine(sample, "README.md")); var adjudication = File.ReadAllText(Path.Combine(sample, "ADJUDICATION.md")); var site = File.ReadAllText(Path.Combine(Root, "docs/index.html"));
        Json.Check(readme.Contains("not an automatic\n> functional-C# converter or a correctness proof system", StringComparison.Ordinal) && site.Contains("CSharpAssay 0.2.0-rc.1", StringComparison.Ordinal), "GoF positioning/version changed.");
        foreach (var id in new[] { "CSAF0001", "CSAD0002", "CSAI0003" })
        {
            Json.Check(adjudication.Contains(id, StringComparison.Ordinal) && site.Contains(id, StringComparison.Ordinal), "Missing adjudication/documentation: " + id);
        }

        foreach (var item in expected.A("reports"))
        {
            var report = ValidReport(Path.Combine(Root, item.S("path")), item.S("verdict"), item.B("authoritative"), item.I("projects")); var evidence = report["evidence"]!; var findings = evidence.A("findings").ToList();
            Json.Check(evidence.A("rules").Select(r => r.S("id")).ToHashSet(StringComparer.Ordinal).SetEquals(Catalogue), "Rule catalogue changed."); RequiredRules(evidence);
            foreach (var wanted in item.A("findings"))
            {
                var match = findings.FirstOrDefault(f => f.S("ruleId") == wanted.S("ruleId") && f.S("message").Contains(wanted.S("messageContains"), StringComparison.Ordinal) && f!["location"].S("path") == wanted.S("path") && f["location"].I("startLine") == wanted.I("startLine") && f.S("severity") == wanted.S("severity") && f.S("certainty") == wanted.S("certainty") && f.S("disposition") == wanted.S("disposition"));
                Json.Check(match is not null, "Missing reviewed GoF finding."); _ = findings.Remove(match);
            }
            Json.Check(findings.Count == 0 && evidence.A("tests").Sum(t => t.I("passed")) == item.I("testsPassed") && evidence.A("tests").All(t => t.S("outcome") == "passed" && t.I("failed") == 0), "Unexpected GoF findings/tests.");
            var sarif = Json.Read(Path.Combine(Root, item.S("sarifPath"))); Json.Check(sarif.S("version") == "2.1.0" && sarif.A("runs").Count == 1 && JsonNode.DeepEquals(Counts(sarif.A("runs")[0].A("results")), Counts(evidence.A("findings"))), "SARIF finding inventory differs from JSON.");
        }
    }
    private static void RequiredRules(JsonNode evidence)
    {
        var rules = evidence.A("rules").Where(r => r.B("required")).ToArray(); Json.Check(rules.Select(r => r.S("id")).ToHashSet(StringComparer.Ordinal).SetEquals(Required) && rules.All(r => r.S("outcome") == "completed"), "Required rule inventory or completion changed.");
    }
    private static void PackageBaseline(JsonNode expected, string sample, string[] folders)
    {
        Json.Check(JsonNode.DeepEquals(expected["packageBaseline"], Json.Create(("tool", "CsAssay.Tool@0.2.0-rc.1"), ("analyzer", "CsAssay.Analyzers@0.2.0-rc.1"))), "Package baseline changed.");
        var tool = Json.Read(Path.Combine(Root, ".config/dotnet-tools.json"))["tools"]!["csassay.tool"]!; Json.Check(tool.S("version") == "0.2.0-rc.1" && !tool.B("rollForward"), "Tool manifest version/roll-forward changed.");
        foreach (var folder in folders) { var dependency = Json.Read(Path.Combine(sample, folder, "packages.lock.json"))["dependencies"]!["net11.0"]!["CsAssay.Analyzers"]!; Json.Check(dependency.S("type") == "Direct" && dependency.S("resolved") == "0.2.0-rc.1", "Analyzer package lock changed."); }
        foreach (var project in Files(sample).Where(p => Path.GetExtension(p) == ".csproj"))
        {
            Json.Check(!File.ReadAllText(project).Contains("/CSharpAssay", StringComparison.Ordinal), "Sample contains engine source reference.");
        }
    }
    public static void Shape()
    {
        var sample = Path.Combine(Root, "samples/40-functional-shape-v0.1"); var expectedPath = Path.Combine(sample, "expected-evidence.json"); var expected = Json.Read(expectedPath); var inputs = Json.Read(Path.Combine(sample, "evidence-inputs.json")); var output = Path.Combine(Root, "evidence/generated/functional-shape-v0.1");
        var report = ValidReport(Path.Combine(output, "verify.json"), "pass", true, expected.I("projects")); var evidence = report["evidence"]!; PackageBaseline(expected, sample, ["src/Shape.Domain", "src/Shape.Application"]); RequiredRules(evidence);
        Json.Check(report.S("schemaVersion") == "1.2.0" && evidence.A("projects").All(p => p.A("compilerDiagnostics").Count == 0) && evidence.A("findings").All(f => f.S("disposition") != "block"), "Shape compiler/schema/blocking evidence changed.");
        var recorded = new JsonObject(); foreach (var source in evidence.A("sources").Where(s => (s.S("path").StartsWith("src/", StringComparison.Ordinal) || s.S("path").StartsWith("tests/", StringComparison.Ordinal)) && !s.S("path").Contains("/obj/", StringComparison.Ordinal) && !s.S("path").Contains("/bin/", StringComparison.Ordinal)))
        {
            recorded[source.S("path")] = source.S("sha256");
        }

        var current = new JsonObject(); foreach (var path in sourceArray.SelectMany(f => Files(Path.Combine(sample, f))).Where(p => Path.GetExtension(p) == ".cs"))
        {
            current[Relative(sample, path)] = Json.Sha(path);
        }

        Json.Check(JsonNode.DeepEquals(recorded, current), "Shape source inventory/hash is stale.");
        var policyHash = Json.Sha(Path.Combine(sample, ".csassay.json")); Json.Check(inputs.S("policySha256") == policyHash && evidence["policy"].S("sha256") == policyHash && inputs.S("expectedEvidenceSha256") == Json.Sha(expectedPath), "Shape policy/expectation input lock is stale.");
        var projects = new JsonObject(); foreach (var path in Files(sample).Where(p => Path.GetExtension(p) == ".csproj").Append(Path.Combine(sample, "Shape.slnx")))
        {
            projects[Relative(sample, path)] = Json.Sha(path);
        }

        Json.Check(JsonNode.DeepEquals(projects, inputs["projectFiles"]), "Shape project input lock is stale.");
        var tests = new JsonObject(); foreach (var test in evidence.A("tests")) { Json.Check(test.S("outcome") == "passed" && test.I("failed") == 0 && test.I("skipped") == 0, "Shape test gate failed."); tests[Path.GetFileNameWithoutExtension(test.S("input"))] = test.I("passed"); }
        Json.Check(JsonNode.DeepEquals(tests, expected["tests"]), "Shape test totals changed.");
        static JsonNode Identity(JsonNode f)
        {
            return Json.Create(("ruleId", f.S("ruleId")), ("path", f["location"].S("path")), ("disposition", f.S("disposition")), ("fingerprint", f.S("fingerprint")));
        }

        var actual = Json.Array(evidence.A("findings").Select(f => Identity(f!)).OrderBy(f => f.S("fingerprint"), StringComparer.Ordinal)); var wanted = Json.Array(expected.A("findings").OrderBy(f => f.S("fingerprint"), StringComparer.Ordinal)); Json.Check(JsonNode.DeepEquals(actual, wanted), "Shape finding identity changed.");
        var sarif = Json.Read(Path.Combine(output, "verify.sarif")); var sarifFindings = Json.Array(sarif.A("runs").SelectMany(r => r.A("results")).Select(f => Json.Create(("ruleId", f.S("ruleId")), ("path", f!.A("locations")[0]!["physicalLocation"]!["artifactLocation"].S("uri")), ("disposition", f!["properties"].S("disposition").ToLowerInvariant()), ("fingerprint", f!["partialFingerprints"].S("csAssay/v1")))).OrderBy(f => f.S("fingerprint"), StringComparer.Ordinal)); Json.Check(JsonNode.DeepEquals(sarifFindings, wanted), "Shape SARIF identity changed.");
    }
    public static void AssertCsvAdvisories(JsonNode evidence, JsonArray expected)
    {
        Json.Check(evidence["suppressions"] is JsonArray suppressions && suppressions.Count == 0, "CSV suppressions changed.");
        var findings = evidence["findings"] as JsonArray ?? throw new InvalidDataException("Missing CSV finding inventory.");
        var reviewed = new JsonArray();
        string[] legacyFields = ["ruleId", "message", "severity", "certainty", "disposition", "suppressed", "project", "targetFramework", "location", "fingerprint"];
        foreach (var entry in findings)
        {
            var finding = entry ?? throw new InvalidDataException("Missing CSV finding.");
            Json.Check(finding.S("disposition") == "advise" && finding["suppressed"]?.GetValue<bool>() == false && finding.S("scope") == "shell" && finding.S("identityPath").Length > 0 && finding.S("identityPath") == finding["location"].S("path"), "CSV advisory scope, identity or authorization changed.");
            var original = new JsonObject(); foreach (var field in legacyFields)
            {
                original[field] = (finding[field] ?? throw new InvalidDataException("Missing reviewed CSV finding field: " + field)).DeepClone();
            }
            reviewed.Add(original);
        }
        Json.Check(JsonNode.DeepEquals(reviewed, expected), "CSV reviewed fields or finding count changed.");
    }
    public static JsonObject Csv()
    {
        var sample = Path.Combine(Root, SampleInvestigations.Csv.RelativePath); var output = Path.Combine(Root, "evidence/generated/csvhelper"); var expected = Json.Read(Path.Combine(sample, "expected-evidence.json")); var receipt = ValidReport(Path.Combine(output, "verify.json"), "pass", true, expected.I("projects")); var value = receipt["evidence"]!;
        AssertCsvAdvisories(value, expected.A("findings"));
        Json.Check(value.A("tests").Count == 1, "CSV test selection changed."); var test = value.A("tests")[0]; Json.Check(test.I("total") == expected.I("tests") && test.I("passed") == expected.I("tests") && test.I("failed") == 0 && test.I("skipped") == 0 && test.I("notRun") == 0, "CSV tests did not complete cleanly.");
        var reportPath = Path.Combine(output, "functional/investigations.json"); var investigations = Json.Read(reportPath); Json.Check(investigations.S("executionStatus") == "completed" && investigations["summary"]!["after"].I("killed") == 23 && investigations["summary"]!["after"].I("survived") == 1, "CSV mutation outcomes changed."); SampleInvestigations.AssertBindings(investigations["inputSha256"]!); Json.Check(JsonNode.DeepEquals(investigations["inputSha256"], SampleInvestigations.Bindings(SampleInvestigations.Csv)), "CSV input inventory changed.");
        var streaming = Json.Read(Path.Combine(output, "streaming/benchmark.json")); Json.Check(streaming.S("executionStatus") == "completed", "Streaming benchmark incomplete."); SampleInvestigations.AssertBindings(streaming["inputSha256"]!);
        var provider = Json.Read(Path.Combine(output, "advice-comparison.json")); Json.Check(provider.S("executionStatus") is "not_requested" or "unavailable_missing_keys" or "completed" or "stopped_after_failure" && provider.S("investigationsSha256") == Json.Sha(reportPath) && provider.S("scriptSha256") == Json.Sha(Path.Combine(Root, "eng/Assay.Tooling/AdviceEvaluation.cs")), "CSV advice provenance changed.");
        var coverage = investigations["coverage"]!.DeepClone().AsObject(); foreach (var (_, item) in coverage)
        {
            _ = item!.AsObject().Remove("lineHits");
        }

        var summary = Json.Create(("schema", "csv-functional-validation/0.1"), ("executionStatus", "completed"), ("verdict", "pass"), ("authoritative", true), ("projects", expected.I("projects")), ("tests", expected.I("tests")), ("advisories", value.A("findings").Count), ("missingEvidence", 0), ("toolFailures", 0), ("receiptSha256", Json.Sha(Path.Combine(output, "verify.json"))), ("sarifSha256", Json.Sha(Path.Combine(output, "verify.sarif"))), ("repeatJsonAndSarifByteIdentical", true), ("mutationSummary", investigations["summary"]), ("coverage", coverage), ("investigationReportSha256", Json.Sha(reportPath)), ("providerRequests", provider.I("requestsAttempted")), ("providerEvaluationStatus", provider.S("executionStatus")), ("reviewStatus", expected.S("reviewStatus")), ("reviewLedgerSha256", Json.Sha(Path.Combine(sample, "review-cases.json"))), ("benchmarkReportSha256", Json.Sha(Path.Combine(output, "streaming/benchmark.json"))), ("benchmarkSummaries", streaming["summaries"]), ("scope", "Functional consumer sample; upstream source assessment remains separate."));
        Json.Write(Path.Combine(output, "validation.json"), summary); return summary;
    }
}
