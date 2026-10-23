namespace Assay.Tooling;

public static class MilestoneComparison
{
    private static readonly string[] sourceArray = new[] { "before", "after" };
    private static readonly string[] values = new[] { "Independent human adjudication and usefulness study", "Explicit live provider evaluation", "Held-out consumer and broader framework qualification" };

    public static JsonObject Run()
    {
        var output = Path.Combine(Root, "evidence/generated/csvhelper/milestones"); var path = Path.Combine(output, "status.json"); Json.Write(path, Json.Create(("schema", "csv-milestone-status/0.1"), ("executionStatus", "running")));
        try
        {
            var archived = Path.Combine(output, "before"); var beforePath = Path.Combine(archived, "evidence/hefty/comparison.json"); var afterPath = Path.Combine(Path.GetDirectoryName(output)!, "hefty/comparison.json"); var before = Json.Read(beforePath); var after = Json.Read(afterPath);
            foreach (var report in new[] { before, after })
            {
                Json.Check(report.S("executionStatus") == "completed" && report.A("results").Count == 18 && report.A("comparisons").Count == 8, "Two complete heavyweight experiments required.");
            }

            Json.Check(JsonNode.DeepEquals(before["fixtures"], after["fixtures"]) && before.S("csvHelperAssemblySha256") == after.S("csvHelperAssemblySha256"), "Frozen fixtures/original binary changed.");
            foreach (var (name, checksum) in before["inputSha256"]!.AsObject())
            {
                Json.Check(Json.Sha(Inside(Path.Combine(archived, "inputs"), name)) == checksum!.GetValue<string>(), "Archived baseline source altered.");
            }

            Json.Check(before.S("assayValidationSha256") == Json.Sha(Path.Combine(archived, "evidence/validation.json")) && before.S("assayReceiptSha256") == Json.Sha(Path.Combine(archived, "evidence/verify.json")), "Archived verification evidence differs from measured baseline.");
            foreach (var row in before.A("results"))
            {
                var raw = Path.Combine(archived, "evidence/hefty/raw", row.S("mode"), row.S("case") + "-" + row.S("pipeline") + ".json"); Json.Check(Json.Sha(raw) == row.S("rawReportSha256"), "Archived raw measurement altered."); var fixture = before.A("fixtures").Single(f => f!["Specification"].S("Id") == row.S("case")); _ = Benchmarks.ValidateChild(Json.Read(raw), fixture!, row.S("pipeline"), row.S("mode"));
            }
            SampleInvestigations.AssertBindings(after["inputSha256"]!); Json.Check(JsonNode.DeepEquals(SampleInvestigations.Artifacts(Path.Combine(Root, SampleInvestigations.Csv.RelativePath)), after["artifactSha256"]), "Current binaries differ from measured artifacts.");
            var comparisons = new JsonArray();
            foreach (var current in after.A("results"))
            {
                var old = before.A("results").Single(r => r.S("mode") == current.S("mode") && r.S("case") == current.S("case") && r.S("pipeline") == current.S("pipeline")); Json.Check(JsonNode.DeepEquals(old!["result"], current!["result"]), "Optimization changed complete/prefix output.");
                foreach (var key in new[] { "runtime", "architecture", "os", "serverGc", "processorCount", "tieredCompilation", "tieredPgo", "buildConfiguration" })
                {
                    Json.Check(JsonNode.DeepEquals(current["context"]![key], old["context"]![key]), "Measurement context changed: " + key);
                }

                comparisons.Add(Json.Create(("mode", current.S("mode")), ("case", current.S("case")), ("pipeline", current.S("pipeline")), ("completeOutputEquivalent", current.B("completeOutputEquivalent")), ("beforeMilliseconds", old["medianMilliseconds"]), ("afterMilliseconds", current["medianMilliseconds"]), ("afterToBeforeTimeRatio", Math.Round(Benchmarks.Number(current["medianMilliseconds"]) / Benchmarks.Number(old["medianMilliseconds"]), 4)), ("beforeAllocatedBytes", old["medianAllocatedBytes"]), ("afterAllocatedBytes", current["medianAllocatedBytes"]), ("afterToBeforeAllocationRatio", Math.Round(Benchmarks.Number(current["medianAllocatedBytes"]) / Benchmarks.Number(old["medianAllocatedBytes"]), 4))));
            }
            var profiles = sourceArray.Select(phase => Json.Read(Path.Combine(output, phase, "allocation-profile.json"))).ToArray(); Json.Check(profiles[0].S("probeSha256") == profiles[1].S("probeSha256") && profiles[0].S("runtime") == profiles[1].S("runtime") && profiles.All(p => p.S("tieredCompilation") == "0"), "Allocation probe/runtime changed.");
            var stages = new JsonArray(); foreach (var stage in profiles[0].A("samples").Select(s => s.S("stage")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) { var values = profiles.Select(p => p.A("samples").Where(s => s.S("stage") == stage).Select(s => Benchmarks.Number(s!["bytesPerOperation"])).ToArray()).ToArray(); Json.Check(values.All(v => v.Length == 3), "Missing allocation probe repetitions."); stages.Add(Json.Create(("stage", stage), ("beforeBytes", Benchmarks.Median(values[0])), ("afterBytes", Benchmarks.Median(values[1])))); }
            var result = Json.Create(("schema", "csv-milestone-comparison/0.1"), ("executionStatus", "completed"), ("toolSha256", Json.Sha(Tool)), ("beforeReportSha256", Json.Sha(beforePath)), ("afterReportSha256", Json.Sha(afterPath)), ("comparisons", comparisons), ("allocationStages", stages), ("authority", "Unchanged physical inputs and outputs; no significance or production superiority claim."), ("performanceGateApplied", false)); Json.Write(Path.Combine(output, "comparison.json"), result);
            var directory = Path.GetDirectoryName(output)!; var validation = Path.Combine(directory, "validation.json"); var roundtrip = Json.Read(Path.Combine(directory, "roundtrip/comparison.json")); var packet = Json.Read(Path.Combine(directory, "review/packet.json")); Json.Check(roundtrip.S("executionStatus") == "completed" && roundtrip.S("validationSha256") == Json.Sha(validation) && packet.S("validationSha256") == Json.Sha(validation), "Roundtrip/review packet must match completed current gate.");
            Json.Write(path, Json.Create(("schema", "csv-milestone-status/0.1"), ("executionStatus", "completed-local-work"), ("authoritativeSampleVerdict", Json.Read(validation).S("verdict")), ("externalRemaining", values), ("authority", "Executed local evidence only; unexecuted human/provider work remains open."))); return result;
        }
        catch (Exception error) { Json.Write(path, Json.Create(("schema", "csv-milestone-status/0.1"), ("executionStatus", "failed"), ("failure", error.Message))); throw; }
    }
}
