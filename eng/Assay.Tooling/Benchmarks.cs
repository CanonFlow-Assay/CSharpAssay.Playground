namespace Assay.Tooling;

public static partial class Benchmarks
{
    public static decimal Number(JsonNode? node) { Json.Check(node?.GetValueKind() == JsonValueKind.Number && decimal.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out _), "Expected a finite numeric measurement."); return decimal.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture); }
    private static long Integer(JsonNode? node) { Json.Check(node?.GetValueKind() == JsonValueKind.Number && long.TryParse(node.ToJsonString(), out _), "Expected an integer measurement."); return long.Parse(node.ToJsonString(), CultureInfo.InvariantCulture); }
    public static decimal Median(IEnumerable<decimal> values) { var sorted = values.Order().ToArray(); Json.Check(sorted.Length > 0, "No measured runs."); return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2; }
    public static JsonArray ValidateStreaming(JsonNode raw)
    {
        Json.Check(raw.S("schema") == "csv-stream-benchmark/0.1" && raw.S("executionStatus") == "completed", "Incomplete benchmark.");
        var rows = raw.A("samples"); Json.Check(rows.Count == 28, "Expected both pipelines, two sizes, two warmups and five measured runs.");
        var expected = new HashSet<(string Mode, long Rows, long Iteration)>();
        foreach (var mode in new[] { "direct", "functional" })
        {
            foreach (var count in new[] { 10000, 100000 })
            {
                for (var i = -2; i < 5; i++)
                {
                    _ = expected.Add((mode, count, i));
                }
            }
        }

        var observed = rows.Select(r => (r.S("mode"), Integer(r!["rows"]), Integer(r["iteration"]))).ToArray();
        Json.Check(observed.Distinct().Count() == rows.Count && expected.SetEquals(observed), "Missing/duplicate benchmark identities.");
        var digests = new Dictionary<long, JsonNode>();
        foreach (var row in rows)
        {
            var count = Integer(row!["rows"]); var iteration = Integer(row["iteration"]);
            Json.Check(Number(row["elapsedMilliseconds"]) > 0 && Integer(row["allocatedBytes"]) >= 0 && row.B("warmup") == (iteration < 0), "Invalid measurement/warmup classification.");
            var digest = row["digest"] ?? throw new InvalidDataException("Missing digest.");
            Json.Check(digest.AsObject().Select(p => p.Key).ToHashSet(StringComparer.Ordinal).SetEquals(["Accepted", "Rejected", "IdSum", "AmountSum", "ActiveCount", "NoteCharacters", "Fingerprint", "IssueCount", "Stop", "StopRecord"]), "Incomplete content digest.");
            Json.Check(ulong.TryParse(digest["Fingerprint"]?.ToJsonString(), out _) && Integer(digest["NoteCharacters"]) > 0 && Integer(digest["Accepted"]) == count && Integer(digest["Rejected"]) == 0 && Integer(digest["IssueCount"]) == 0 && Integer(digest["Stop"]) == 0 && Integer(digest["StopRecord"]) == 0 && Integer(digest["IdSum"]) == count * (count + 1) / 2 && Number(digest["AmountSum"]) == count * (count + 1) / 200m && Integer(digest["ActiveCount"]) == count / 2, "Output violates independent aggregate formulas.");
            Json.Check(!digests.TryGetValue(count, out var previous) || JsonNode.DeepEquals(previous, digest), "Pipelines/iterations produced different content."); digests[count] = digest;
        }
        Json.Check(raw.S("tieredCompilation") == "0" && raw.S("buildConfiguration") == "Release" && raw.S("runtime").Length > 0 && raw.S("os").Length > 0 && raw.S("scope").Length > 0, "Missing controlled Release/runtime context.");
        return rows;
    }
    public static JsonArray ValidateManifest(JsonNode manifest, JsonNode plan, string directory)
    {
        Json.Check(manifest.S("Schema") == "csv-hefty-fixtures/0.1", "Unsupported fixture manifest.");
        var fixtures = manifest.A("Fixtures"); var cases = plan.A("cases").ToDictionary(c => c.S("id"), c => c!, StringComparer.Ordinal);
        Json.Check(fixtures.Count == cases.Count && fixtures.Select(f => f!["Specification"].S("Id")).ToHashSet(StringComparer.Ordinal).SetEquals(cases.Keys), "Missing/duplicate/unrequested physical fixtures.");
        long total = 0;
        foreach (var fixture in fixtures)
        {
            var declared = fixture!["Specification"]!; var item = cases[declared.S("Id")];
            var expected = Json.Create(("Id", item.S("id")), ("Rows", item.I("rows")), ("NoteCharacters", item.I("noteCharacters")), ("Unicode", item.B("unicode")), ("Culture", item.S("culture")), ("InvalidEvery", item.I("invalidEvery")));
            Json.Check(JsonNode.DeepEquals(declared, expected) && fixture.S("FileName") == item.S("id") + ".csv", "Fixture differs from the preregistered workload.");
            var path = Inside(directory, fixture.S("FileName"));
            Json.Check(File.Exists(path) && new FileInfo(path).Length == fixture.L("FileBytes") && fixture.L("FileBytes") > 0 && string.Equals(Json.Sha(path), fixture.S("FileSha256"), StringComparison.OrdinalIgnoreCase), "Physical file bytes/hash differ.");
            var count = (long)item.I("rows"); var interval = item.I("invalidEvery"); var rejected = interval > 0 ? count / interval : 0; var sum = (count * (count + 1) / 2) - (interval > 0 ? interval * rejected * (rejected + 1) / 2 : 0); var totals = fixture["Expected"]!["Totals"]!;
            Json.Check(Integer(totals["Accepted"]) == count - rejected && Integer(totals["Rejected"]) == rejected && Integer(totals["IssueCount"]) == 3 * rejected && Integer(totals["IdSum"]) == sum && Number(totals["AmountSum"]) == sum / 100m && Integer(totals["NoteCharacters"]) == (count - rejected) * item.I("noteCharacters") && Integer(totals["ActiveCount"]) == (count / 2) - (interval % 2 == 1 ? rejected / 2 : rejected) && Integer(totals["Stop"]) == 0 && Integer(totals["StopRecord"]) == 0, "Physical fixture violates independent formulas.");
            foreach (var key in new[] { "Expected", "OriginalExpected" })
            {
                Json.Check(MyRegex.IsMatch(fixture[key].S("OutputSha256")), "Missing full content SHA-256.");
            }

            if (interval == 0)
            {
                Json.Check(JsonNode.DeepEquals(fixture["OriginalExpected"], fixture["Expected"]), "Valid original reference differs from full reference.");
            }
            else
            {
                var prefix = fixture["OriginalExpected"]!; var values = prefix["Totals"]!; var n = interval - 1L;
                Json.Check(prefix.I("Failure") == 1 && Integer(values["Accepted"]) == n && Integer(values["Rejected"]) == 0 && Integer(values["IdSum"]) == n * (n + 1) / 2 && Integer(values["Stop"]) == 1 && Integer(values["StopRecord"]) == interval, "Fail-fast prefix reference is inconsistent.");
            }
            total += fixture.L("FileBytes");
        }
        Json.Check(total <= plan.L("maximumTotalFileBytes"), "Fixture volume exceeds the disk budget."); return fixtures;
    }
    public static JsonArray ValidateChild(JsonNode raw, JsonNode fixture, string pipeline, string mode)
    {
        Json.Check(raw.S("schema") == "csv-hefty-child/0.1" && raw.S("executionStatus") == "completed" && raw.S("caseId") == fixture["Specification"].S("Id") && raw.S("pipeline") == pipeline, "Unexpected/incomplete child execution.");
        var setting = mode == "tiered-pgo" ? "1" : "0";
        Json.Check(raw.S("tieredCompilation") == setting && raw.S("tieredPgo") == setting && raw.S("buildConfiguration") == "Release" && raw.S("runtime").Length > 0 && raw.S("os").Length > 0 && Integer(raw["peakWorkingSetBytes"]) > 0, "Missing runtime/memory context.");
        var rows = raw.A("samples"); Json.Check(rows.Count == 5 && rows.Select(r => Integer(r!["iteration"])).ToHashSet().SetEquals([-2, -1, 0, 1, 2]), "Expected two warmups and three distinct measured runs.");
        var expected = fixture[pipeline == "Original" ? "OriginalExpected" : "Expected"];
        foreach (var row in rows)
        {
            Json.Check(row.B("warmup") == (Integer(row!["iteration"]) < 0) && Number(row["elapsedMilliseconds"]) > 0 && Integer(row["allocatedBytes"]) > 0 && sourceArray.All(g => Integer(row[g]) >= 0), "Invalid duration/allocation/GC/warmup evidence.");
            Json.Check(JsonNode.DeepEquals(row["result"], expected) && row.B("equivalentToCompleteExpected") == JsonNode.DeepEquals(expected, fixture["Expected"]), "Output/equivalence differs from complete or prefix reference.");
        }
        return Json.Array(rows.Where(r => !r.B("warmup")));
    }
    public static JsonArray ValidateRoundtrip(JsonNode raw)
    {
        Json.Check(raw.S("schema") == "csv-roundtrip-workload/0.1" && raw.S("executionStatus") == "completed" && raw.S("tieredCompilation") == "1" && raw.S("tieredPgo") == "1", "Incomplete roundtrip/runtime settings.");
        var fixtures = raw.A("fixtures"); var samples = raw.A("samples");
        Json.Check(fixtures.Count == 2 && fixtures.Select(f => f!["Specification"].S("Id")).ToHashSet(StringComparer.Ordinal).SetEquals(["roundtrip-invariant", "roundtrip-german"]) && samples.Count == 16, "Missing/additional roundtrip fixtures or runs.");
        foreach (var fixture in fixtures)
        {
            var id = fixture!["Specification"].S("Id");
            Json.Check(JsonNode.DeepEquals(fixture["Specification"], Json.Create(("Id", id), ("Rows", 50000), ("NoteCharacters", 128), ("Unicode", true), ("Culture", id.EndsWith("german", StringComparison.Ordinal) ? "de-DE" : ""), ("InvalidEvery", 0))), "Roundtrip fixture changed.");
            var hashes = new HashSet<string>(StringComparer.Ordinal); var sizes = new HashSet<long>();
            foreach (var pipeline in new[] { "Original", "Assayed" })
            {
                var rows = samples.Where(r => r.S("caseId") == id && r.S("pipeline") == pipeline).ToArray();
                Json.Check(rows.Length == 4 && rows.Select(r => Integer(r!["iteration"])).ToHashSet().SetEquals([-1, 0, 1, 2]), "Missing/duplicate roundtrip iterations.");
                foreach (var row in rows)
                {
                    Json.Check(JsonNode.DeepEquals(row!["digest"], fixture["Expected"]) && row.B("warmup") == (Integer(row["iteration"]) < 0) && Number(row["elapsedMilliseconds"]) > 0 && Integer(row["allocatedBytes"]) > 0 && Regex.IsMatch(row.S("outputSha256"), "^[A-F0-9]{64}$") && Integer(row["outputBytes"]) > 0, "Invalid roundtrip content/measurement evidence.");
                    _ = hashes.Add(row.S("outputSha256")); _ = sizes.Add(Integer(row["outputBytes"]));
                }
            }
            Json.Check(hashes.Count == 1 && sizes.Count == 1, "Original/assayed physical output differs.");
        }
        return fixtures;
    }
    private static string Sample => Path.Combine(Root, SampleInvestigations.Csv.RelativePath);
    private static string Workload => Path.Combine(Sample, "workloads/CsvFunctional.Workloads/bin/Release/net11.0/CsvFunctional.Workloads.dll");
    private static JsonObject Summary(IEnumerable<JsonNode?> rows, int count)
    {
        var selected = rows.ToArray(); var times = selected.Select(r => Number(r!["elapsedMilliseconds"])).ToArray(); var allocations = selected.Select(r => Number(r!["allocatedBytes"])).ToArray(); var median = Median(times);
        return Json.Create(("measuredRuns", selected.Length), ("medianMilliseconds", Math.Round(median, 3)), ("minMilliseconds", Math.Round(times.Min(), 3)), ("maxMilliseconds", Math.Round(times.Max(), 3)), ("medianRowsPerSecond", Math.Round(count * 1000m / median)), ("medianAllocatedBytes", Median(allocations)), ("medianAllocatedBytesPerRow", Math.Round(Median(allocations) / count, 2)));
    }
    public static async Task<JsonObject> Streaming()
    {
        var output = Path.Combine(Root, "evidence/generated/csvhelper/streaming"); var path = Path.Combine(output, "benchmark.json");
        var report = Json.Create(("schema", "csv-streaming-validation/0.1"), ("executionStatus", "running"), ("performanceGateApplied", false), ("providerRequests", 0), ("authority", "Local diagnostic benchmark; no production or superiority qualification.")); Json.Write(path, report);
        try
        {
            var project = Path.Combine(Sample, "workloads/CsvFunctional.Workloads/CsvFunctional.Workloads.csproj");
            (await Processes.Run(["dotnet", "restore", project, "--locked-mode"], Root, Path.Combine(output, "restore.log"))).Require("Workload restore");
            (await Processes.Run(["dotnet", "build", project, "--no-restore", "-c", "Release"], Root, Path.Combine(output, "build.log"))).Require("Workload build");
            var bindings = SampleInvestigations.Bindings(SampleInvestigations.Csv); var artifacts = SampleInvestigations.Artifacts(Sample);
            var execution = await Processes.Run(["dotnet", Workload, "--benchmark"], Root, Path.Combine(output, "benchmark-execution.log"), 180, new Dictionary<string, string> { ["DOTNET_TieredCompilation"] = "0" }); execution.Require("Benchmark execution");
            var raw = Json.Parse(execution.Output); var rows = ValidateStreaming(raw); Json.Write(Path.Combine(output, "benchmark-raw.json"), raw);
            var summaries = new JsonArray();
            foreach (var count in new[] { 10000, 100000 })
            {
                var modes = new JsonObject(); foreach (var mode in new[] { "direct", "functional" })
                {
                    modes[mode] = Summary(rows.Where(r => r.I("rows") == count && r.S("mode") == mode && !r.B("warmup")), count);
                }

                summaries.Add(Json.Create(("rows", count), ("pipelines", modes), ("functionalToDirectTimeRatio", Math.Round(Number(modes["functional"]!["medianMilliseconds"]) / Number(modes["direct"]!["medianMilliseconds"]), 3)), ("functionalToDirectAllocationRatio", Math.Round(Number(modes["functional"]!["medianAllocatedBytes"]) / Number(modes["direct"]!["medianAllocatedBytes"]), 3))));
            }
            SampleInvestigations.AssertBindings(bindings); Json.Check(JsonNode.DeepEquals(bindings, SampleInvestigations.Bindings(SampleInvestigations.Csv)) && JsonNode.DeepEquals(artifacts, SampleInvestigations.Artifacts(Sample)), "Inputs/artifacts changed during measurement.");
            var context = raw.DeepClone().AsObject(); _ = context.Remove("samples"); report["context"] = context; report["inputSha256"] = bindings; report["artifactSha256"] = artifacts; report["summaries"] = summaries; report["rawReportSha256"] = Json.Sha(Path.Combine(output, "benchmark-raw.json")); report["requirementsSha256"] = Json.Sha(Path.Combine(Sample, "workloads/requirements.json")); report["executionStatus"] = "completed"; Json.Write(path, report); return report;
        }
        catch (Exception failure) { report["executionStatus"] = "failed"; report["failure"] = failure.Message; Json.Write(path, report); throw; }
    }
    public static void RequireVerifiedSample()
    {
        var validation = Json.Read(Path.Combine(Root, "evidence/generated/csvhelper/validation.json")); var investigations = Json.Read(Path.Combine(Root, SampleInvestigations.Csv.Output, "investigations.json"));
        Json.Check(validation.S("executionStatus") == "completed" && validation.S("verdict") == "pass" && validation.B("authoritative") && investigations.S("executionStatus") == "completed" && JsonNode.DeepEquals(investigations["inputSha256"], SampleInvestigations.Bindings(SampleInvestigations.Csv)), "Run the current complete authoritative CSV sample gate first.");
        SampleInvestigations.AssertBindings(investigations["inputSha256"]!);
    }
    public static async Task<JsonObject> Hefty(bool reuse)
    {
        RequireVerifiedSample();
        var output = Path.Combine(Root, "evidence/generated/csvhelper/hefty"); var path = Path.Combine(output, "comparison.json");
        var report = Json.Create(("schema", "csv-hefty-comparison/0.1"), ("executionStatus", "running"), ("results", new JsonArray()), ("comparisons", new JsonArray()), ("providerRequests", 0), ("performanceGateApplied", false), ("authority", "Synthetic warm-cache file comparison; original package versus verified consumer, not a whole-library fork.")); Json.Write(path, report);
        try
        {
            var plan = Json.Read(Path.Combine(Sample, "workloads/hefty-cases.json"));
            Json.Check(plan.S("schema") == "csv-hefty-plan/0.1" && plan.Strings("runtimeModes").SequenceEqual(["tiered-pgo", "non-tiered"]) && plan.I("warmupsPerChild") == 2 && plan.I("measuredRunsPerChild") == 3 && plan.A("cases").Count is >= 1 and <= 4 && plan.A("cases").Sum(c => (long)c.I("rows") * ((6 * c.I("noteCharacters")) + 96)) <= plan.L("maximumTotalFileBytes"), "Unsupported/beyond-budget hefty plan.");
            var bindings = SampleInvestigations.Bindings(SampleInvestigations.Csv); var artifacts = SampleInvestigations.Artifacts(Sample); var packageHash = Json.Sha(Path.Combine(Path.GetDirectoryName(Workload)!, "CsvHelper.dll"));
            report["csvHelperAssemblySha256"] = packageHash.ToUpperInvariant();
            report["assayValidationSha256"] = Json.Sha(Path.Combine(Root, "evidence/generated/csvhelper/validation.json"));
            report["assayReceiptSha256"] = Json.Sha(Path.Combine(Root, "evidence/generated/csvhelper/verify.json"));
            report["csvHelperPackage"] = Json.Read(Path.Combine(Sample, "upstream.json"))["package"]!.DeepClone();
            var data = Path.Combine(output, "data"); _ = Directory.CreateDirectory(data);
            if (!reuse || !File.Exists(Path.Combine(data, "manifest.json")))
            {
                (await Processes.Run(["dotnet", Workload, "--hefty-generate", data], Root, Path.Combine(output, "generation.log"), 300)).Require("Physical fixture generation");
            }

            var fixtures = ValidateManifest(Json.Read(Path.Combine(data, "manifest.json")), plan, data); var manifestHash = Json.Sha(Path.Combine(data, "manifest.json"));
            report["fixtures"] = fixtures.DeepClone(); report["totalFileBytes"] = fixtures.Sum(f => f.L("FileBytes")); report["manifestSha256"] = manifestHash; report["inputSha256"] = bindings.DeepClone(); report["artifactSha256"] = artifacts.DeepClone();
            var modeIndex = 0;
            foreach (var mode in plan.Strings("runtimeModes"))
            {
                var index = 0;
                foreach (var fixture in fixtures)
                {
                    var id = fixture!["Specification"].S("Id"); List<string> pipelines = fixture["Specification"].I("InvalidEvery") > 0 ? ["Original", "ResilientOriginal", "Assayed"] : ["Original", "Assayed"]; if ((modeIndex + index++) % 2 == 1)
                    {
                        pipelines.Reverse();
                    }

                    foreach (var pipeline in pipelines)
                    {
                        var rawPath = Path.Combine(output, "raw", mode, id + "-" + pipeline + ".json");
                        var setting = mode == "tiered-pgo" ? "1" : "0";
                        var execution = await Processes.Run(["dotnet", Workload, "--hefty-run", Path.Combine(data, "manifest.json"), id, pipeline], Root, Path.ChangeExtension(rawPath, ".log"), plan.I("maximumChildSeconds"), new Dictionary<string, string> { ["DOTNET_TieredCompilation"] = setting, ["DOTNET_TieredPGO"] = setting }); execution.Require("Hefty " + id + "/" + pipeline);
                        var raw = Json.Parse(execution.Output); var selected = ValidateChild(raw, fixture, pipeline, mode); Json.Write(rawPath, raw);
                        var summary = Summary(selected, fixture["Specification"].I("Rows")); var comparable = selected[0].B("equivalentToCompleteExpected");
                        summary["mode"] = mode; summary["case"] = id; summary["pipeline"] = pipeline; summary["completeOutputEquivalent"] = comparable; summary["result"] = selected[0]!["result"]!.DeepClone(); summary["processPeakRssMiB"] = Math.Round(Number(raw["peakWorkingSetBytes"]) / 1048576m, 2); summary["rawReportSha256"] = Json.Sha(rawPath);
                        var context = raw.DeepClone().AsObject(); context.Remove("samples"); summary["context"] = context;
                        summary["allocatedBytesPerRow"] = comparable ? Number(summary["medianAllocatedBytesPerRow"]) : null;
                        summary["medianMiBPerSecond"] = comparable ? Math.Round(fixture.L("FileBytes") / 1048576m * 1000m / Number(summary["medianMilliseconds"]), 2) : null;
                        if (!comparable) { summary["medianRowsPerSecond"] = null; summary["medianAllocatedBytesPerRow"] = null; }
                        report.A("results").Add(summary); Json.Write(path, report); Console.WriteLine($"{mode} {id} {pipeline} {summary["medianMilliseconds"]}ms complete={comparable}");
                    }
                    var rows = report.A("results").Where(r => r.S("case") == id && r.S("mode") == mode && r.B("completeOutputEquivalent")).ToArray(); var baseline = rows.Single(r => r.S("pipeline") == (fixture["Specification"].I("InvalidEvery") > 0 ? "ResilientOriginal" : "Original")); var assayed = rows.Single(r => r.S("pipeline") == "Assayed");
                    Json.Check(JsonNode.DeepEquals(assayed!["result"], baseline!["result"]), "Matched comparison must have identical complete output.");
                    report.A("comparisons").Add(Json.Create(("mode", mode), ("case", id), ("baseline", baseline.S("pipeline")), ("assayedToOriginalTimeRatio", Math.Round(Number(assayed["medianMilliseconds"]) / Number(baseline["medianMilliseconds"]), 3)), ("assayedToOriginalAllocationRatio", Math.Round(Number(assayed["medianAllocatedBytes"]) / Number(baseline["medianAllocatedBytes"]), 3))));
                }
                modeIndex++;
            }
            _ = ValidateManifest(Json.Read(Path.Combine(data, "manifest.json")), plan, data); SampleInvestigations.AssertBindings(bindings);
            Json.Check(Json.Sha(Path.Combine(data, "manifest.json")) == manifestHash && JsonNode.DeepEquals(bindings, SampleInvestigations.Bindings(SampleInvestigations.Csv)) && JsonNode.DeepEquals(artifacts, SampleInvestigations.Artifacts(Sample)) && Json.Sha(Path.Combine(Path.GetDirectoryName(Workload)!, "CsvHelper.dll")) == packageHash, "Fixture/source/package/artifact changed during measurement.");
            report["executionStatus"] = "completed"; Json.Write(path, report); return report;
        }
        catch (Exception failure) { report["executionStatus"] = "failed"; report["failure"] = failure.Message; Json.Write(path, report); throw; }
    }
    public static async Task<JsonObject> Roundtrip()
    {
        RequireVerifiedSample(); var output = Path.Combine(Root, "evidence/generated/csvhelper/roundtrip"); var path = Path.Combine(output, "comparison.json"); var data = Path.Combine(output, "data"); _ = Directory.CreateDirectory(data);
        var report = Json.Create(("schema", "csv-roundtrip-comparison/0.1"), ("executionStatus", "running"), ("performanceGateApplied", false)); Json.Write(path, report);
        try
        {
            var bindings = SampleInvestigations.Bindings(SampleInvestigations.Csv); var artifacts = SampleInvestigations.Artifacts(Sample);
            var execution = await Processes.Run(["dotnet", Workload, "--roundtrip", data], Root, Path.Combine(output, "execution.log"), 300, new Dictionary<string, string> { ["DOTNET_TieredCompilation"] = "1", ["DOTNET_TieredPGO"] = "1" }); execution.Require("Physical roundtrip workload");
            var raw = Json.Parse(execution.Output); var fixtures = ValidateRoundtrip(raw); Json.Write(Path.Combine(output, "raw.json"), raw);
            var cases = Json.Array(fixtures.Select(f => Json.Create(("id", f!["Specification"].S("Id")), ("rows", 50000), ("noteCharacters", 128), ("unicode", true), ("culture", f["Specification"].S("Culture")), ("invalidEvery", 0))));
            _ = ValidateManifest(Json.Create(("Schema", "csv-hefty-fixtures/0.1"), ("Fixtures", fixtures)), Json.Create(("cases", cases), ("maximumTotalFileBytes", 1073741824)), data);
            var comparisons = new JsonArray();
            foreach (var fixture in fixtures)
            {
                var id = fixture!["Specification"].S("Id"); var summaries = new JsonObject();
                foreach (var pipeline in new[] { "Original", "Assayed" })
                {
                    var rows = raw.A("samples").Where(r => r.S("caseId") == id && r.S("pipeline") == pipeline).ToArray(); var file = Path.Combine(data, id + "-" + pipeline + ".output.csv");
                    Json.Check(string.Equals(Json.Sha(file), rows[0].S("outputSha256"), StringComparison.OrdinalIgnoreCase) && new FileInfo(file).Length == rows[0].L("outputBytes"), "Physical roundtrip output differs from digest.");
                    summaries[pipeline] = Summary(rows.Where(r => !r.B("warmup")), 50000);
                }
                comparisons.Add(Json.Create(("case", id), ("pipelines", summaries), ("assayedToOriginalTimeRatio", Math.Round(Number(summaries["Assayed"]!["medianMilliseconds"]) / Number(summaries["Original"]!["medianMilliseconds"]), 3)), ("assayedToOriginalAllocationRatio", Math.Round(Number(summaries["Assayed"]!["medianAllocatedBytes"]) / Number(summaries["Original"]!["medianAllocatedBytes"]), 3))));
            }
            SampleInvestigations.AssertBindings(bindings); Json.Check(JsonNode.DeepEquals(artifacts, SampleInvestigations.Artifacts(Sample)), "Artifacts changed during roundtrip.");
            report["executionStatus"] = "completed"; report["comparisons"] = comparisons; report["inputSha256"] = bindings; report["artifactSha256"] = artifacts; report["rawReportSha256"] = Json.Sha(Path.Combine(output, "raw.json")); report["validationSha256"] = Json.Sha(Path.Combine(Root, "evidence/generated/csvhelper/validation.json")); Json.Write(path, report); return report;
        }
        catch (Exception failure) { report["executionStatus"] = "failed"; report["failure"] = failure.Message; Json.Write(path, report); throw; }
    }

    [GeneratedRegex("^[A-F0-9]{64}$")]
    private static partial Regex MyRegex { get; }

    private static readonly string[] sourceArray = new[] { "gen0", "gen1", "gen2" };
}
