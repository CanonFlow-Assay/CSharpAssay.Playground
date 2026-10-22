namespace Assay.Tooling.Tests;

public sealed class CsvBoundaryTests
{
    private static JsonObject Streaming()
    {
        var rows = new JsonArray();
        foreach (var count in new[] { 10000, 100000 })
        {
            foreach (var mode in new[] { "direct", "functional" })
            {
                for (var i = -2; i < 5; i++)
                {
                    rows.Add(Json.Create(("mode", mode), ("rows", count), ("iteration", i), ("warmup", i < 0), ("elapsedMilliseconds", 1), ("allocatedBytes", 1000), ("digest", Json.Create(("Accepted", count), ("Rejected", 0), ("IssueCount", 0), ("Stop", 0), ("StopRecord", 0), ("IdSum", (long)count * (count + 1) / 2), ("AmountSum", (long)count * (count + 1) / 200m), ("ActiveCount", count / 2), ("NoteCharacters", count * 8), ("Fingerprint", 42)))));
                }
            }
        }

        return Json.Create(("schema", "csv-stream-benchmark/0.1"), ("executionStatus", "completed"), ("samples", rows), ("runtime", "fixture"), ("os", "fixture"), ("scope", "failure-boundary fixture"), ("tieredCompilation", "0"), ("buildConfiguration", "Release"));
    }
    private static JsonObject Fixture()
    {
        var result = Json.Create(("Totals", Json.Create(("Accepted", 20), ("Rejected", 0), ("IdSum", 210), ("AmountSum", 2.1m), ("ActiveCount", 10), ("NoteCharacters", 320), ("Fingerprint", 42), ("IssueCount", 0), ("Stop", 0), ("StopRecord", 0))), ("OutputSha256", new string('A', 64)), ("Failure", 0));
        return Json.Create(("Specification", Json.Create(("Id", "small"), ("Rows", 20), ("NoteCharacters", 16), ("Unicode", false), ("Culture", ""), ("InvalidEvery", 0))), ("FileName", "small.csv"), ("Expected", result), ("OriginalExpected", result));
    }
    private static JsonObject Child()
    {
        var fixture = Fixture(); return Json.Create(("schema", "csv-hefty-child/0.1"), ("executionStatus", "completed"), ("caseId", "small"), ("pipeline", "Original"), ("tieredCompilation", "1"), ("tieredPgo", "1"), ("buildConfiguration", "Release"), ("runtime", "fixture"), ("os", "fixture"), ("peakWorkingSetBytes", 1024), ("samples", Enumerable.Range(-2, 5).Select(i => Json.Create(("iteration", i), ("warmup", i < 0), ("elapsedMilliseconds", 10), ("allocatedBytes", 1000), ("result", fixture["Expected"]), ("equivalentToCompleteExpected", true), ("gen0", 0), ("gen1", 0), ("gen2", 0)))));
    }
    private static JsonObject Roundtrip()
    {
        var fixtures = new JsonArray(); var samples = new JsonArray();
        foreach (var (id, culture) in new[] { ("roundtrip-invariant", ""), ("roundtrip-german", "de-DE") })
        {
            fixtures.Add(Json.Create(("Specification", Json.Create(("Id", id), ("Rows", 50000), ("NoteCharacters", 128), ("Unicode", true), ("Culture", culture), ("InvalidEvery", 0))), ("Expected", Json.Create(("sentinel", "complete")))));
            foreach (var pipeline in new[] { "Original", "Assayed" })
            {
                for (var i = -1; i < 3; i++)
                {
                    samples.Add(Json.Create(("caseId", id), ("pipeline", pipeline), ("iteration", i), ("warmup", i < 0), ("digest", Json.Create(("sentinel", "complete"))), ("elapsedMilliseconds", 10), ("allocatedBytes", 1000), ("outputBytes", 100), ("outputSha256", new string('A', 64))));
                }
            }
        }
        return Json.Create(("schema", "csv-roundtrip-workload/0.1"), ("executionStatus", "completed"), ("tieredCompilation", "1"), ("tieredPgo", "1"), ("fixtures", fixtures), ("samples", samples));
    }
    [Fact] public void MissingStreamingSamplesRejected() { var raw = Streaming(); raw.A("samples").RemoveAt(0); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateStreaming(raw)); }
    [Fact] public void DuplicateStreamingCannotReplaceRun() { var raw = Streaming(); raw.A("samples")[0] = raw.A("samples")[1]!.DeepClone(); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateStreaming(raw)); }
    [Fact] public void WrongAggregateInvalidatesTiming() { var raw = Streaming(); raw.A("samples")[0]!["digest"]!["IdSum"] = 1; _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateStreaming(raw)); }
    [Fact] public void EqualTotalsCannotHideDifferentNotes() { var raw = Streaming(); raw.A("samples")[0]!["digest"]!["Fingerprint"] = 99; _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateStreaming(raw)); }
    [Fact]
    public void NonfiniteZeroBooleanTimingRejected()
    {
        foreach (var value in new object[] { 0, true, "NaN", "Infinity" }) { var raw = Streaming(); raw.A("samples")[0]!["elapsedMilliseconds"] = Json.Node(value); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateStreaming(raw)); }
    }
    [Fact] public void MissingContentDigestRejected() { var raw = Streaming(); _ = raw.A("samples")[0]!["digest"]!.AsObject().Remove("Fingerprint"); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateStreaming(raw)); }
    [Fact]
    public void ProviderNeedsExplicitLive()
    {
        _ = Assert.Throws<InvalidDataException>(() => AdviceEvaluation.ValidateRequest(false, ["openai"], 6, 3, 6));
    }

    [Fact]
    public void ProviderBudgetBeforeCall()
    {
        _ = Assert.Throws<InvalidDataException>(() => AdviceEvaluation.ValidateRequest(true, ["openai", "jev"], 5, 6, 6));
    }

    [Fact]
    public void DuplicateProviderRejected()
    {
        _ = Assert.Throws<InvalidDataException>(() => AdviceEvaluation.ValidateRequest(true, ["openai", "openai"], 6, 6, 6));
    }

    [Fact] public void OfflineComparisonNeedsNoModel() { AdviceEvaluation.ValidateRequest(false, [], 6, 0, 6); Assert.Equal(28, Benchmarks.ValidateStreaming(Streaming()).Count); }
    [Fact] public void MissingHeftyMeasuredRunRejected() { var raw = Child(); raw.A("samples").RemoveAt(0); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateChild(raw, Fixture(), "Original", "tiered-pgo")); }
    [Fact] public void DuplicateHeftyCannotReplaceRun() { var raw = Child(); raw.A("samples")[0] = raw.A("samples")[1]!.DeepClone(); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateChild(raw, Fixture(), "Original", "tiered-pgo")); }
    [Fact] public void EqualHeftyCountsCannotHideContentCorruption() { var raw = Child(); raw.A("samples")[0]!["result"]!["OutputSha256"] = new string('B', 64); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateChild(raw, Fixture(), "Original", "tiered-pgo")); }
    [Fact] public void PartialOutputCannotClaimCompleteEquivalence() { var raw = Child(); raw.A("samples")[0]!["equivalentToCompleteExpected"] = false; _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateChild(raw, Fixture(), "Original", "tiered-pgo")); }
    [Fact] public void WrongRuntimeModeRejected() { var raw = Child(); raw["tieredCompilation"] = "0"; _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateChild(raw, Fixture(), "Original", "tiered-pgo")); }
    [Fact] public void MissingPeakMemoryContextRejected() { var raw = Child(); raw["peakWorkingSetBytes"] = 0; _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateChild(raw, Fixture(), "Original", "tiered-pgo")); }
    [Fact] public void InvalidHeftyMeasurementsRejected() { foreach (var value in new object[] { 0, true, "NaN" }) { var raw = Child(); raw.A("samples")[0]!["elapsedMilliseconds"] = Json.Node(value); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateChild(raw, Fixture(), "Original", "tiered-pgo")); } }
    [Fact]
    public void ChangedPhysicalFileRejected()
    {
        using var folder = new TemporaryWorkspace(); var path = Path.Combine(folder.Path, "small.csv"); File.WriteAllText(path, "fixture bytes"); var item = Fixture(); item["FileBytes"] = new FileInfo(path).Length; item["FileSha256"] = Json.Sha(path).ToUpperInvariant(); var manifest = Json.Create(("Schema", "csv-hefty-fixtures/0.1"), ("Fixtures", new[] { item })); var plan = Json.Create(("cases", new[] { Json.Create(("id", "small"), ("rows", 20), ("noteCharacters", 16), ("unicode", false), ("culture", ""), ("invalidEvery", 0)) }), ("maximumTotalFileBytes", 1024)); _ = Benchmarks.ValidateManifest(manifest, plan, folder.Path); File.WriteAllText(path, "changed bytes"); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateManifest(manifest, plan, folder.Path));
    }
    [Fact] public void MissingRoundtripRunRejected() { var raw = Roundtrip(); raw.A("samples").RemoveAt(0); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateRoundtrip(raw)); }
    [Fact] public void DuplicateRoundtripRunRejected() { var raw = Roundtrip(); raw.A("samples")[0] = raw.A("samples")[1]!.DeepClone(); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateRoundtrip(raw)); }
    [Fact] public void ChangedPhysicalOutputWithEqualValuesRejected() { var raw = Roundtrip(); raw.A("samples")[0]!["outputSha256"] = new string('B', 64); _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateRoundtrip(raw)); }
    [Fact] public void IncompleteRoundtripOutputRejected() { var raw = Roundtrip(); raw.A("samples")[0]!["digest"]!["sentinel"] = "partial"; _ = Assert.Throws<InvalidDataException>(() => Benchmarks.ValidateRoundtrip(raw)); }
    private static JsonNode Response()
    {
        return Json.Parse(/*lang=json,strict*/ """{"packetSha256":"bound","reviewer":"fixture reviewer","reviews":[{"id":"one","decision":"accept","rationale":"fixture rationale"}]}""");
    }

    private static JsonObject Review(JsonNode response)
    {
        return HumanReview.ValidateResponses(response, Json.Parse(/*lang=json,strict*/ """{"items":[{"id":"one"}]}"""), "bound");
    }

    [Fact] public void MissingReviewerRejected() { var r = Response(); r["reviewer"] = ""; _ = Assert.Throws<InvalidDataException>(() => Review(r)); }
    [Fact] public void StalePacketRejected() { var r = Response(); r["packetSha256"] = "stale"; _ = Assert.Throws<InvalidDataException>(() => Review(r)); }
    [Fact] public void IncompleteReviewItemsRejected() { var r = Response(); r["reviews"] = new JsonArray(); _ = Assert.Throws<InvalidDataException>(() => Review(r)); }
    [Fact] public void DuplicateReviewItemsRejected() { var r = Response(); r.A("reviews").Add(r.A("reviews")[0]!.DeepClone()); _ = Assert.Throws<InvalidDataException>(() => Review(r)); }
    [Fact] public void HumanFeedbackCannotGrantPass() { var r = Response(); r.A("reviews")[0]!["decision"] = "Pass"; _ = Assert.Throws<InvalidDataException>(() => Review(r)); }
    [Fact] public void ResponsesCannotChangeVerdictOrDenominator() { var result = Review(Response()); Assert.False(result.B("verificationVerdictChanged")); Assert.False(result.B("mutationDenominatorChanged")); Assert.Contains("Self-reported", result.S("identityVerification"), StringComparison.Ordinal); }
    [Fact]
    public void MutationRequiresExactlyOneSpan()
    {
        var item = Json.Create(("id", "case"), ("before", "value"), ("after", "other")); foreach (var text in new[] { "absent", "value value" })
        {
            _ = Assert.Throws<InvalidDataException>(() => SampleInvestigations.ExactEdit(text, item));
        }
    }
    [Fact]
    public void ZeroExecutedTestsCannotPass()
    {
        Assert.Equal("ToolFailure", ApplicationRunner.TestRecord("/missing/csv-tests.xml", new(ProcessState.Passed, 0, ""), 79).S("state"));
    }

    [Fact]
    public void FailedProcessCannotReusePassingReport()
    {
        using var folder = new TemporaryWorkspace(); var path = Path.Combine(folder.Path, "tests.xml"); File.WriteAllText(path, "<assemblies><assembly total=\"1\" passed=\"1\" failed=\"0\"><collection><test name=\"old\" result=\"Pass\"/></collection></assembly></assemblies>"); Assert.Equal("ToolFailure", ApplicationRunner.TestRecord(path, new(ProcessState.Failed, 2, ""), 1).S("state"));
    }
    [Fact]
    public void PartialSelectionCannotPass()
    {
        using var folder = new TemporaryWorkspace(); var path = Path.Combine(folder.Path, "tests.xml"); File.WriteAllText(path, "<assemblies><assembly total=\"79\" passed=\"79\" failed=\"0\"><collection><test name=\"only-one\" result=\"Pass\"/></collection></assembly></assemblies>"); Assert.Equal("ToolFailure", ApplicationRunner.TestRecord(path, new(ProcessState.Passed, 0, ""), 79).S("state"));
    }
    [Fact]
    public void CoverageCannotOmitAdapter()
    {
        using var folder = new TemporaryWorkspace(); var path = Path.Combine(folder.Path, "coverage.xml"); File.WriteAllText(path, "<coverage lines-covered=\"1\" lines-valid=\"1\" branches-covered=\"1\" branches-valid=\"1\"><packages><package name=\"CsvFunctional.Domain\"/></packages></coverage>"); _ = Assert.Throws<InvalidDataException>(() => SampleInvestigations.CoverageRecord(path, folder.Path, SampleInvestigations.Csv.Modules));
    }
    [Fact]
    public void EmptyCoverageCannotPass()
    {
        using var folder = new TemporaryWorkspace(); var path = Path.Combine(folder.Path, "coverage.xml"); File.WriteAllText(path, "<coverage lines-covered=\"0\" lines-valid=\"0\" branches-covered=\"0\" branches-valid=\"0\"><packages><package name=\"CsvFunctional.Domain\"/><package name=\"CsvFunctional.Adapter\"/></packages></coverage>"); _ = Assert.Throws<InvalidDataException>(() => SampleInvestigations.CoverageRecord(path, folder.Path, SampleInvestigations.Csv.Modules));
    }
    [Fact]
    public async Task StaleSuccessDeletedBeforeExecution()
    {
        using var folder = new TemporaryWorkspace(); var report = Path.Combine(folder.Path, "tests.xml"); File.WriteAllText(report, "stale"); var result = await ApplicationRunner.Tests(folder.Path, Json.Create(("testDll", "missing.dll"), ("minimumTests", 79)), folder.Path); Assert.False(File.Exists(report)); Assert.Equal("ToolFailure", result.S("state"));
    }
    [Fact]
    public void ChangedInputBindingRejectsCompletion()
    {
        var root = EnvironmentPaths.Root; using var folder = new TemporaryWorkspace(".boundary-", Path.Combine(root, "evidence/generated")); var file = Path.Combine(folder.Path, "input.cs"); File.WriteAllText(file, "original"); var bindings = Json.Create((EnvironmentPaths.Relative(root, file), Json.Sha(file))); File.WriteAllText(file, "changed"); _ = Assert.Throws<InvalidDataException>(() => SampleInvestigations.AssertBindings(bindings));
    }
}
