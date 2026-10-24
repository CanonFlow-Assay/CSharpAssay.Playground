namespace Assay.Tooling.Tests;

public sealed class CsvReviewedFindingTests
{
    private static JsonObject Finding() => Json.Create(("ruleId", "CSAF0002"), ("message", "Reviewed heuristic observation"), ("severity", "info"), ("certainty", "heuristic"), ("disposition", "advise"), ("suppressed", false), ("project", "CsvFunctional.Tests"), ("targetFramework", "net11.0"), ("location", Json.Create(("path", "tests/Specimen.cs"), ("startLine", 3), ("startColumn", 2), ("endLine", 3), ("endColumn", 9))), ("fingerprint", Json.Hash("synthetic-reviewed-observation")));
    private static JsonObject Evidence()
    {
        var finding = Finding(); finding["scope"] = "shell"; finding["identityPath"] = "tests/Specimen.cs";
        finding["futureAdditiveContext"] = "May be ignored by an older field projection";
        return Json.Create(("findings", new JsonArray(finding)), ("suppressions", new JsonArray()));
    }

    [Fact]
    public void Additive_context_does_not_change_any_reviewed_finding_field()
    {
        EvidenceAssertions.AssertCsvAdvisories(Evidence(), new JsonArray(Finding()));
    }

    [Theory]
    [InlineData("severity")]
    [InlineData("message")]
    [InlineData("fingerprint")]
    [InlineData("scope")]
    [InlineData("identityPath")]
    [InlineData("suppression")]
    [InlineData("count")]
    [InlineData("location")]
    public void Changed_or_hidden_observations_cannot_satisfy_the_reviewed_inventory(string change)
    {
        var evidence = Evidence(); var finding = evidence.A("findings")[0] ?? throw new InvalidDataException("Missing fixture finding.");
        switch (change)
        {
            case "severity": finding["severity"] = "error"; break;
            case "message": finding["message"] = "Different observation"; break;
            case "fingerprint": finding["fingerprint"] = Json.Hash("different-observation"); break;
            case "scope": finding["scope"] = "core"; break;
            case "identityPath": finding["identityPath"] = "other/Specimen.cs"; break;
            case "suppression": finding["suppressed"] = true; break;
            case "count": evidence.A("findings").Add(finding.DeepClone()); break;
            case "location": finding["location"]!["startLine"] = 4; break;
        }
        Assert.Throws<InvalidDataException>(() => EvidenceAssertions.AssertCsvAdvisories(evidence, new JsonArray(Finding())));
    }
}
