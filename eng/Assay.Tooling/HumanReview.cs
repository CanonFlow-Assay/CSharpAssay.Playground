namespace Assay.Tooling;

public static class HumanReview
{
    public static JsonObject ValidateResponses(JsonNode responses, JsonNode packet, string packetSha)
    {
        Json.Check(responses.S("packetSha256") == packetSha && !string.IsNullOrWhiteSpace(responses.S("reviewer")), "A self-reported reviewer and this exact packet identity are required.");
        var rows = responses.A("reviews"); var required = packet.A("items").Select(i => i.S("id")).ToHashSet(StringComparer.Ordinal);
        Json.Check(rows.Count == required.Count && rows.Select(r => r.S("id")).ToHashSet(StringComparer.Ordinal).SetEquals(required), "Exactly one response per assigned item is required.");
        foreach (var row in rows)
        {
            Json.Check(row.S("decision") is "accept" or "request-change" or "uncertain" && !string.IsNullOrWhiteSpace(row.S("rationale")), "Each item needs a registered decision and rationale; no Pass or waiver authority.");
        }

        return Json.Create(("schema", "csv-human-review-responses/0.1"), ("status", "responses-provided"), ("identityVerification", "Self-reported; this tool cannot authenticate identity or independence."), ("reviewer", responses.S("reviewer")), ("packetSha256", packetSha), ("reviews", rows), ("verificationVerdictChanged", false), ("mutationDenominatorChanged", false), ("authority", "Review feedback only; no automatic waiver, equivalence exclusion or Pass."));
    }
    private static JsonArray Excerpt(string path, int line)
    {
        var lines = File.ReadAllLines(path); return Json.Array(Enumerable.Range(Math.Max(0, line - 3), Math.Max(0, Math.Min(lines.Length, line + 2) - Math.Max(0, line - 3))).Select(i => Json.Create(("line", i + 1), ("text", lines[i]))));
    }
    public static JsonObject Prepare(string? responses)
    {
        var output = Path.Combine(Root, "evidence/generated/csvhelper/review"); _ = Directory.CreateDirectory(output); var packetPath = Path.Combine(output, "packet.json");
        if (responses is not null) { var result = ValidateResponses(Json.Read(responses), Json.Read(packetPath), Json.Sha(packetPath)); Json.Write(Path.Combine(output, "responses-report.json"), result); return result; }
        Benchmarks.RequireVerifiedSample();
        var directory = Path.GetDirectoryName(output)!; var sample = Path.Combine(Root, SampleInvestigations.Csv.RelativePath); var investigationPath = Path.Combine(directory, "functional/investigations.json"); var investigations = Json.Read(investigationPath); var corpus = Json.Read(Path.Combine(sample, "mutation-cases.json")); var expected = Json.Read(Path.Combine(sample, "expected-evidence.json")); var ledger = Json.Read(Path.Combine(sample, "review-cases.json")); var items = new JsonArray();
        foreach (var item in corpus.A("cases"))
        {
            var path = Inside(sample, item.S("path")); var observed = investigations.A("results").Single(r => r.S("id") == item.S("id"));
            items.Add(Json.Create(("id", "mutation:" + item.S("id")), ("kind", "mutation"), ("requirement", item.S("requirement")), ("source", item.S("path")), ("sourceSha256", Json.Sha(path)), ("excerpt", Excerpt(path, item.I("line"))), ("mutation", Json.Create(("before", item.S("before")), ("after", item.S("after")))), ("observedBefore", observed!["phases"]!["before"].S("state")), ("observedAfter", observed["phases"]!["after"].S("state")), ("question", "Is the requirement independently supported? A survivor remains in the denominator.")));
        }
        foreach (var finding in expected.A("findings"))
        {
            var location = finding!["location"]!; var path = Inside(sample, location.S("path")); var review = ledger.A("advisories").Single(r => r.S("fingerprint") == finding.S("fingerprint"));
            items.Add(Json.Create(("id", "advisory:" + finding.S("fingerprint")), ("kind", "advisory"), ("finding", finding), ("sourceSha256", Json.Sha(path)), ("excerpt", Excerpt(path, location.I("startLine"))), ("agentRationale", review.S("rationale")), ("question", "Does this finding warrant a source change or retention with rationale?")));
        }
        var packet = Json.Create(("schema", "csv-human-review-packet/0.1"), ("humanAdjudication", "pending"), ("authority", "Agent-prepared handoff; no automatic human adjudication."), ("validationSha256", Json.Sha(Path.Combine(directory, "validation.json"))), ("investigationsSha256", Json.Sha(investigationPath)), ("inputSha256", investigations["inputSha256"]), ("items", items), ("survivor", ledger["survivor"]), ("frameworkDecision", ledger["frameworkDecision"])); Json.Write(packetPath, packet);
        Json.Write(Path.Combine(output, "response-template.json"), Json.Create(("packetSha256", Json.Sha(packetPath)), ("reviewer", ""), ("reviews", items.Select(i => Json.Create(("id", i.S("id")), ("decision", ""), ("rationale", ""))))));
        Json.Write(Path.Combine(output, "usefulness-protocol.json"), Json.Create(("schema", "csv-advice-study-protocol/0.1"), ("status", "not-executed"), ("authority", "Protocol only; no participants, timings or usefulness outcomes collected."), ("primaryOutcome", "Seconds to an investigation an independent domain reviewer rates useful."), ("design", "Preregister held-out cases, reviewers, assignment and analysis; counterbalance baseline/provider arms and hide provider names."), ("reporting", "Retain failures, unavailable arms and unsupported suggestions; keep verification authority unchanged.")));
        return Json.Create(("status", "packet-prepared"), ("items", items.Count), ("humanAdjudication", "pending"), ("usefulnessStudy", "not-executed"));
    }
}
