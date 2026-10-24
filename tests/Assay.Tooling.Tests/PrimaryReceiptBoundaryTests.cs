namespace Assay.Tooling.Tests;

public sealed class PrimaryReceiptBoundaryTests
{
    private static JsonObject Expected() => Json.Create(("verdict", "fail"), ("exitCode", 1), ("authoritative", false), ("verificationMode", "analysis"), ("projects", 1), ("missingEvidenceCounts", Json.Create(("CSASSAY-NULLABLE-FLOW-INCOMPLETE", 2))));
    private static JsonObject Negative() => Json.Create(("verdict", "fail"), ("exitCode", 1), ("evidence", Json.Create(
        ("toolVersion", "0.2.0-rc.1"), ("requestedProfile", "native"), ("profile", "native"), ("authoritative", false), ("verificationMode", "analysis"),
        ("projects", new JsonArray(Json.Create(("targetFramework", "net11.0"), ("profile", "native"), ("profileEvidence", "native"), ("languageVersion", "preview"), ("loaded", true)))),
        ("failures", new JsonArray()),
        ("missingEvidence", new JsonArray(Json.Create(("code", "CSASSAY-NULLABLE-FLOW-INCOMPLETE")), Json.Create(("code", "CSASSAY-NULLABLE-FLOW-INCOMPLETE")))),
        ("findings", new JsonArray(Json.Create(("ruleId", "CSAP0001"), ("disposition", "block"), ("suppressed", false)))))));

    [Fact]
    public void Declared_nullable_negative_remains_a_nonpassing_analysis_with_explicit_gaps()
    {
        EvidenceAssertions.AssertPrimaryReceipt(Negative(), Expected());
    }

    [Theory]
    [InlineData("pass")]
    [InlineData("tool-failure")]
    [InlineData("unexpected-gap")]
    [InlineData("missing-gap")]
    [InlineData("authority")]
    [InlineData("suppressed-integrity")]
    [InlineData("advisory-integrity")]
    [InlineData("execution-failure")]
    [InlineData("verification")]
    public void Unrelated_or_weakened_receipts_cannot_satisfy_a_declared_negative(string change)
    {
        var report = Negative(); var evidence = report["evidence"]!;
        switch (change)
        {
            case "pass": report["verdict"] = "pass"; report["exitCode"] = 0; break;
            case "tool-failure": report["exitCode"] = 3; break;
            case "unexpected-gap": evidence.A("missingEvidence")[0]!["code"] = "UNEXPECTED-LOAD-FAILURE"; break;
            case "missing-gap": evidence.A("missingEvidence").RemoveAt(0); break;
            case "authority": evidence["authoritative"] = true; break;
            case "suppressed-integrity": evidence.A("findings")[0]!["suppressed"] = true; break;
            case "advisory-integrity": evidence.A("findings")[0]!["disposition"] = "advise"; break;
            case "execution-failure": evidence.A("failures").Add(Json.Create(("code", "TOOL-FAILED"))); break;
            case "verification": evidence["verificationMode"] = "verification"; break;
        }
        Assert.Throws<InvalidDataException>(() => EvidenceAssertions.AssertPrimaryReceipt(report, Expected()));
    }
}
