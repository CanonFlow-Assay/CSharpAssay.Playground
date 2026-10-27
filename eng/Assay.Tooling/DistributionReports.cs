namespace Assay.Tooling;

// Consumer of the completed engine receipt. No finding-count gate or second policy evaluation.
public static class DistributionReports
{
    private static readonly string[] Redactions = ["Original private source/tool paths and complete source inventory omitted. Original machine receipt retained locally."];
    public static JsonObject PublicReceipt(JsonNode receipt, string originalDigest, JsonNode? sarif = null)
    {
        var evidence = receipt["evidence"] ?? throw new InvalidDataException("Missing engine evidence.");
        var headline = Headline(receipt);
        string Level(JsonNode? finding) => sarif is null ? "Not projected" : sarif.A("runs")[0].A("results")
            .Single(r => r?["partialFingerprints"].S("csAssay/v1") == finding.S("fingerprint")).S("level");
        var observations = evidence.A("findings").Select(f => Json.Create(("ruleId", f.S("ruleId")),
            ("message", f.S("message")), ("scope", f.S("scope")), ("rawSeverity", f.S("severity")), ("effectiveLevel", Level(f)),
            ("suppressed", f.B("suppressed")), ("path", Path.GetFileName(f?["location"].S("path"))),
            ("line", f?["location"].I("startLine"))));
        return Json.Create(("schema", "csassay-public-derivative/0.1"), ("redactedDerivative", true),
            ("originalSha256", originalDigest), ("redactions", Redactions),
            ("verdict", receipt.S("verdict")), ("exitCode", receipt.I("exitCode")), ("headline", headline),
            ("authoritative", evidence.B("authoritative")), ("verificationMode", evidence.S("verificationMode")),
            ("toolVersion", evidence.S("toolVersion")), ("toolchain", evidence["toolchain"]),
            ("policySha256", evidence["policy"].S("sha256")), ("analyzers", evidence["analyzers"]),
            ("tests", Json.Array(evidence.A("tests").Select(t => Json.Create(("input", Path.GetFileName(t.S("input"))),
                ("required", t.B("required")), ("outcome", t.S("outcome")), ("total", t.I("total")), ("passed", t.I("passed")), ("failed", t.I("failed")), ("skipped", t.I("skipped")))))),
            ("missingEvidenceCount", evidence.A("missingEvidence").Count), ("executionFailureCount", evidence.A("failures").Count),
            ("missingEvidence", Json.Array(evidence.A("missingEvidence").Select(m => Json.Create(("code", m.S("code")), ("message", m.S("message")))))),
            ("findings", Json.Array(observations)));
    }

    public static string Headline(JsonNode receipt)
    {
        var e = receipt["evidence"] ?? throw new InvalidDataException("Missing evidence.");
        var mode = e.S("verificationMode");
        Json.Check(mode is "analysis" or "verification", "Unknown verification mode.");
        var verdict = receipt.S("verdict");
        Json.Check(verdict is "pass" or "fail" or "inconclusive" or "toolFailure", "Unknown verdict.");
        if (verdict == "pass")
            Json.Check(e.A("missingEvidence").Count == 0 && e.A("failures").Count == 0 &&
                e.A("tests").All(t => !t.B("required") || (t.S("outcome") == "passed" && t.I("failed") == 0)), "Contradictory passing receipt.");
        if (verdict == "toolFailure") return "Tool failed";
        if (verdict == "inconclusive") return "Verification incomplete";
        if (mode == "analysis") { Json.Check(!e.B("authoritative"), "Analysis cannot be authoritative."); return "Analysis only"; }
        Json.Check(verdict != "pass" || (e.B("authoritative") && receipt.I("exitCode") == 0), "Pass lacks verification authority.");
        return verdict switch { "pass" => "Verification passed", "fail" => "Verification failed", "inconclusive" => "Verification incomplete", _ => "Tool failed" };
    }

    public static JsonObject Fixture(string id, JsonObject payload)
    {
        Json.Check(Regex.IsMatch(id, "^[A-Za-z0-9-]{1,70}$"), "Invalid public fixture ID.");
        payload["id"] = id;
        var text = payload.ToJsonString();
        return Json.Create(("id", id), ("payload", text), ("sha256", Json.Hash(text)));
    }

    public static JsonObject ValidateFixture(JsonNode entry)
    {
        Json.Check(Json.Hash(entry.S("payload")) == entry.S("sha256"), "Fixture evidence digest mismatch.");
        var payload = Json.Parse(entry.S("payload")).AsObject();
        Json.Check(payload.S("id") == entry.S("id") && payload.S("kind") is "rule" or "scenario", "Fixture identity mismatch.");
        if (payload.S("kind") == "rule") Json.Check(Json.Hash(payload.S("code")) == payload.S("codeSha256"), "Fixture code changed.");
        Json.Check(payload.S("status") == "Recorded example" && payload["observation"] is JsonObject && payload["provenance"] is JsonObject, "Fixture is not completed recorded evidence.");
        return payload;
    }

    public static JsonObject BoundSummary(JsonNode receipt, JsonNode binding, string candidateSource, string originalDigest, string receiptLink)
    {
        Json.Check(Regex.IsMatch(candidateSource, "^[a-f0-9]{40}$") && binding.S("sourceCommit") == candidateSource &&
            binding.S("reportSha256") == originalDigest && binding.B("sourceUnchanged"), "Stale or missing source/report binding.");
        var headline = Headline(receipt); var e = receipt["evidence"]!;
        Json.Check(SafeLink(receiptLink), "Unsafe receipt link.");
        return Json.Create(("schema", "csassay-scoped-summary/0.1"), ("headline", headline), ("verdict", receipt.S("verdict")),
            ("sourceCommit", candidateSource), ("toolVersion", e.S("toolVersion")), ("input", e.S("input")), ("scope", binding.S("scope", e.S("input"))),
            ("authoritative", e.B("authoritative")), ("policySha256", e["policy"].S("sha256")),
            ("requiredTests", e.A("tests").Count == 0 ? "None configured" : string.Join("; ", e.A("tests").Select(t => $"{t.S("outcome")}: {t.I("passed")} passed, {t.I("failed")} failed"))),
            ("observedAt", binding.S("observedAt")), ("receiptSha256", originalDigest), ("receipt", receiptLink),
            ("limits", "Recorded local evidence, not a signature, current-main service or correctness/security certification."));
    }

    public static string Svg(JsonNode summary)
    {
        XNamespace ns = "http://www.w3.org/2000/svg";
        var label = $"CSAssay policy: {summary.S("headline")} · {summary.S("sourceCommit")[..7]}";
        return new XElement(ns + "svg", new XAttribute("viewBox", "0 0 720 94"), new XAttribute("role", "img"),
            new XAttribute("aria-label", label), new XElement(ns + "title", label),
            new XElement(ns + "rect", new XAttribute("width", "720"), new XAttribute("height", "94"), new XAttribute("rx", "10"), new XAttribute("fill", "#202b38")),
            new XElement(ns + "text", new XAttribute("x", "16"), new XAttribute("y", "29"), new XAttribute("fill", "#ffffff"), label),
            new XElement(ns + "text", new XAttribute("x", "16"), new XAttribute("y", "54"), new XAttribute("fill", "#ffffff"),
                $"Tool {summary.S("toolVersion")} · {summary.S("scope")} · required tests: {summary.S("requiredTests")}"),
            new XElement(ns + "text", new XAttribute("x", "16"), new XAttribute("y", "78"), new XAttribute("fill", "#ffffff"),
                $"Observed {summary.S("observedAt")}. Recorded scope only; inspect adjacent evidence index.")).ToString(SaveOptions.DisableFormatting) + "\n";
    }

    public static bool SafeLink(string value) =>
        value.Length is > 0 and < 1000 && !value.Any(c => char.IsControl(c) || c is '\\' or '<' or '>' or '"') &&
        (Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Scheme == "https" && uri.UserInfo.Length == 0 :
            !value.StartsWith('/') && !value.StartsWith('.') && !value.Contains(':') && !value.Split('/').Contains("..", StringComparer.Ordinal));

    public static void WriteBadge(string path, JsonNode summary)
    {
        var svg = Svg(summary); var pending = path + ".partial";
        try { File.WriteAllText(pending, svg); File.Move(pending, path, overwrite: true); }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}
