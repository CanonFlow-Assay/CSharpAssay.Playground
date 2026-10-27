using System.Text.Json;

namespace Assay.Tooling.Tests;

public sealed class DistributionBoundaryTests
{
    private static readonly string Source = new('a', 40);
    private static readonly string Digest = new('b', 64);
    private static JsonObject Receipt(string verdict = "pass", bool authority = true, string mode = "verification") => Json.Create(
        ("verdict", verdict), ("exitCode", verdict == "pass" ? 0 : verdict == "fail" ? 1 : verdict == "toolFailure" ? 3 : 2),
        ("evidence", Json.Create(("verificationMode", mode), ("authoritative", authority), ("input", "Domain.slnx"),
            ("toolVersion", "0.2.0-rc.1"), ("policy", Json.Create(("sha256", Digest))), ("tests", new JsonArray()),
            ("findings", new JsonArray()), ("missingEvidence", new JsonArray()), ("failures", new JsonArray()))));
    private static JsonObject Binding() => Json.Create(("sourceCommit", Source), ("sourceUnchanged", true), ("reportSha256", Digest), ("observedAt", "recorded test time"));

    [Theory] [InlineData("pass", "Verification passed")] [InlineData("fail", "Verification failed")]
    [InlineData("inconclusive", "Verification incomplete")] [InlineData("toolFailure", "Tool failed")]
    public void Recorded_summary_preserves_engine_verdict(string verdict, string expected) => Assert.Equal(expected, DistributionReports.BoundSummary(Receipt(verdict), Binding(), Source, Digest, "receipt.json").S("headline"));

    [Theory] [InlineData("sourceCommit")] [InlineData("reportSha256")] [InlineData("sourceUnchanged")]
    public void Stale_binding_cannot_generate_a_current_pass(string field)
    {
        var binding = Binding(); binding[field] = field == "sourceUnchanged" ? JsonValue.Create(false) : JsonValue.Create("stale");
        Assert.Throws<InvalidDataException>(() => DistributionReports.BoundSummary(Receipt(), binding, Source, Digest, "receipt.json"));
    }
    [Fact] public void Provisional_check_is_analysis_only_even_with_zero_exit()
    {
        var r = Receipt("pass", false, "analysis"); r["evidence"]!.A("findings").Add(Json.Create(("ruleId", "CSAI0001")));
        Assert.Equal("Analysis only", DistributionReports.Headline(r));
    }
    [Fact] public void Absent_authority_cannot_be_a_verification_pass() => Assert.Throws<InvalidDataException>(() => DistributionReports.Headline(Receipt("pass", false)));
    [Fact] public void Incomplete_analysis_keeps_the_engine_incomplete_headline() => Assert.Equal("Verification incomplete", DistributionReports.Headline(Receipt("inconclusive", false, "analysis")));
    [Fact] public void Zero_findings_with_missing_evidence_is_incomplete()
    {
        var r = Receipt("inconclusive", false); r["evidence"]!.A("missingEvidence").Add(Json.Create(("code", "required-test-missing")));
        Assert.Equal("Verification incomplete", DistributionReports.Headline(r));
        r["verdict"] = "pass"; Assert.Throws<InvalidDataException>(() => DistributionReports.Headline(r));
    }
    [Fact] public void Failed_required_tests_cannot_accompany_pass()
    {
        var r = Receipt(); r["evidence"]!.A("tests").Add(Json.Create(("required", true), ("outcome", "failed"), ("failed", 1)));
        Assert.Throws<InvalidDataException>(() => DistributionReports.Headline(r));
    }
    [Theory] [InlineData("javascript:alert(1)")] [InlineData("data:text/html,hello")] [InlineData("//example.org/x")]
    [InlineData("../private.json")] [InlineData("https://user:secret@example.org/x")]
    public void Unsafe_receipt_link_is_rejected(string link) => Assert.Throws<InvalidDataException>(() => DistributionReports.BoundSummary(Receipt(), Binding(), Source, Digest, link));
    [Fact] public void Hostile_badge_text_is_inert_xml()
    {
        var r = Receipt(); r["evidence"]!["input"] = "</text><script>alert(1)</script>";
        var summary = DistributionReports.BoundSummary(r, Binding(), Source, Digest, "receipt.json"); var svg = DistributionReports.Svg(summary);
        var document = XDocument.Parse(svg); Assert.DoesNotContain(document.Descendants(), e => e.Name.LocalName == "script"); Assert.Contains("&lt;script&gt;", svg);
    }
    [Theory] [InlineData("payload")] [InlineData("code")]
    public void Fixture_edit_cannot_retain_recorded_association(string change)
    {
        var payload = Json.Create(("kind", "rule"), ("status", "Recorded example"), ("code", "before"), ("codeSha256", Json.Hash("before")), ("observation", new JsonObject()), ("provenance", new JsonObject()));
        var entry = DistributionReports.Fixture("CSAI0001-before", payload); Assert.Equal("before", DistributionReports.ValidateFixture(entry).S("code"));
        if (change == "payload") entry["payload"] = entry.S("payload") + " ";
        else { var p = Json.Parse(entry.S("payload")); p["code"] = "edited"; entry["payload"] = p.ToJsonString(); entry["sha256"] = Json.Hash(entry.S("payload")); }
        Assert.Throws<InvalidDataException>(() => DistributionReports.ValidateFixture(entry));
    }
    [Fact] public void Missing_or_malformed_evidence_is_not_a_fixture()
    {
        Assert.ThrowsAny<JsonException>(() => Json.Parse("{broken"));
        Assert.Throws<InvalidDataException>(() => DistributionReports.ValidateFixture(DistributionReports.Fixture("D01", Json.Create(("kind", "scenario"), ("status", "Recorded example")))));
    }
    [Theory] [InlineData(".csassay.json")] [InlineData("tests.cs")] [InlineData("packages.lock.json")] [InlineData("global.json")]
    public void Repair_cannot_edit_protected_inputs(string file)
    {
        var baseline = new Dictionary<string, string> { ["Guide.cs"] = "bad", [file] = "protected" }; var candidate = new Dictionary<string, string>(baseline) { [file] = "changed" };
        Assert.Throws<InvalidDataException>(() => RepairExercise.ValidateCandidate(baseline, candidate, Digest, Digest, 1));
    }
    [Fact] public void Badge_output_failure_leaves_no_claimed_artifact_or_staging_file()
    {
        using var app = new TemporaryWorkspace(); var blocked = Path.Combine(app.Path, "badge.svg"); Directory.CreateDirectory(blocked);
        var summary = DistributionReports.BoundSummary(Receipt(), Binding(), Source, Digest, "receipt.json");
        Assert.ThrowsAny<IOException>(() => DistributionReports.WriteBadge(blocked, summary));
        Assert.True(Directory.Exists(blocked)); Assert.False(File.Exists(blocked)); Assert.False(File.Exists(blocked + ".partial"));
    }
    [Theory] [InlineData(0)] [InlineData(3)]
    public void Repair_budget_is_bounded(int round) => Assert.Throws<InvalidDataException>(() => RepairExercise.ValidateCandidate(new Dictionary<string, string>(), new Dictionary<string, string>(), Digest, Digest, round));
    [Theory] [InlineData("bad")] [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public void Malformed_or_stale_feedback_does_not_start_verification(string digest) => Assert.Throws<InvalidDataException>(() => RepairExercise.ValidateCandidate(new Dictionary<string, string>(), new Dictionary<string, string>(), digest, Digest, 1));
    [Fact] public void No_progress_or_added_file_stops_the_repair()
    {
        var before = new Dictionary<string, string> { ["Guide.cs"] = "bad" };
        Assert.Throws<InvalidDataException>(() => RepairExercise.ValidateCandidate(before, before, Digest, Digest, 1));
        Assert.Throws<InvalidDataException>(() => RepairExercise.ValidateCandidate(before, new Dictionary<string, string>(before) { ["new.cs"] = "new" }, Digest, Digest, 1));
    }
    [Fact] public void Same_version_with_different_tool_bytes_is_not_the_prepared_package()
    {
        using var app = new TemporaryWorkspace(); var package = Path.Combine(app.Path, "tool.nupkg"); var installed = Path.Combine(app.Path, "installed"); Directory.CreateDirectory(installed);
        using (var zip = System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Create))
        { using var writer = new StreamWriter(zip.CreateEntry("tools/net11.0/any/cs-assay.dll").Open()); writer.Write("qualified bytes"); }
        File.WriteAllText(Path.Combine(installed, "cs-assay.dll"), "old same-version bytes");
        Assert.Throws<InvalidDataException>(() => PackageTools.ValidateInstalledPayload(package, installed));
        File.WriteAllText(Path.Combine(installed, "cs-assay.dll"), "qualified bytes"); PackageTools.ValidateInstalledPayload(package, installed);
    }
    [Fact] public void A_package_without_the_tool_entry_point_is_rejected()
    {
        using var app = new TemporaryWorkspace(); var package = Path.Combine(app.Path, "empty.nupkg");
        using (System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Create)) { }
        Assert.Throws<InvalidDataException>(() => PackageTools.ValidateInstalledPayload(package, app.Path));
    }
    [Fact] public void Public_finding_level_comes_from_engine_SARIF_not_raw_block_disposition()
    {
        var receipt = Receipt(); receipt["evidence"]!.A("findings").Add(Json.Create(("ruleId", "CSAU0004"), ("severity", "warning"), ("disposition", "block"), ("fingerprint", Digest)));
        var result = Json.Create(("level", "note"), ("partialFingerprints", Json.Create(("csAssay/v1", Digest))));
        var run = Json.Create(("results", new JsonArray(result))); var sarif = Json.Create(("runs", new JsonArray(run)));
        var projection = DistributionReports.PublicReceipt(receipt, Digest, sarif); Assert.Equal("note", projection.A("findings")[0].S("effectiveLevel"));
        Assert.Equal("warning", projection.A("findings")[0].S("rawSeverity"));
    }
}
