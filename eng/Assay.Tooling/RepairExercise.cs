namespace Assay.Tooling;

// One registered source-only exercise. No provider, diagnostic-command execution or arbitrary patch runner.
public static class RepairExercise
{
    public static JsonObject ValidateCandidate(IReadOnlyDictionary<string, string> baseline, IReadOnlyDictionary<string, string> candidate, string feedbackDigest, string expectedFeedbackDigest, int round)
    {
        Json.Check(round is >= 1 and <= 2, "Repair budget exhausted: at most two correction rounds.");
        Json.Check(Regex.IsMatch(feedbackDigest, "^[a-f0-9]{64}$") && feedbackDigest == expectedFeedbackDigest, "Malformed or stale feedback.");
        Json.Check(baseline.Keys.Order(StringComparer.Ordinal).SequenceEqual(candidate.Keys.Order(StringComparer.Ordinal)), "Candidate added or removed protected inputs.");
        var changed = baseline.Keys.Where(k => baseline[k] != candidate[k]).ToArray();
        Json.Check(changed.Length == 1 && changed[0] == "Guide.cs", "Only the registered source file Guide.cs may change; policy/tests/locks/tooling remain protected.");
        return Json.Create(("state", "EligibleForIndependentVerification"), ("changed", changed), ("round", round), ("authority", false));
    }

    public static async Task<JsonObject> Run(string root, string output, JsonNode fixture)
    {
        Json.Check(!Directory.Exists(output), "Retain earlier repair exercises; choose a fresh directory."); Directory.CreateDirectory(output);
        // baseline source is the mechanically extracted canonical after-example.
        var good = fixture.S("after"); var bad = fixture.S("before");
        await DistributionQualification.WriteProject(output, good);
        async Task<JsonNode> Check(string label)
        {
            (await Processes.Run(["dotnet", "build", "Example.csproj", "--no-restore", "-c", "Release"], output, Path.Combine(output, label + "-build.log"))).Require("Independent repair build");
            var path = Path.Combine(output, label + ".json");
            var run = await Processes.Run(["dotnet", PackageTools.PackedTool, "verify", Path.Combine(output, "Example.csproj"), "--policy", Path.Combine(output, ".csassay.json"), "--json", path], root, Path.Combine(output, label + ".log"));
            var receipt = Json.Read(path); Json.Check(run.ExitCode == receipt.I("exitCode"), "Verifier output/exit mismatch."); return receipt;
        }
        var baseline = await Check("baseline"); Json.Check(baseline.S("verdict") == "pass" && baseline["evidence"]!.B("authoritative"), "Repair baseline must pass independently.");
        File.WriteAllText(Path.Combine(output, "Guide.cs"), bad);
        var fault = await Check("fault"); Json.Check(fault.S("verdict") == "fail", "Registered mutable fault was not rejected.");
        File.WriteAllText(Path.Combine(output, "fault.cs.txt"), bad);
        var feedback = Json.Sha(Path.Combine(output, "fault.json"));
        Dictionary<string, string> Snapshot() => Files(output).Where(p => Path.GetExtension(p) is ".cs" or ".csproj" || Path.GetFileName(p) is ".csassay.json" or "packages.lock.json" or "Directory.Build.props" or "Directory.Build.targets" or "global.json")
            .ToDictionary(p => Relative(output, p), File.ReadAllText, StringComparer.Ordinal);
        var snapshot = Snapshot();
        var observations = new JsonArray();
        // The first edit removes the setter but introduces a different admitted mutable-collection finding.
        var attempted = bad.Replace("decimal Total { get; set; }", "System.Collections.Generic.List<decimal> Total { get; init; } = new();", StringComparison.Ordinal);
        Json.Check(attempted != bad, "Registered repair span changed.");
        var failed = new Dictionary<string, string>(snapshot, StringComparer.Ordinal) { ["Guide.cs"] = attempted };
        _ = ValidateCandidate(snapshot, failed, feedback, feedback, 1);
        File.WriteAllText(Path.Combine(output, "Guide.cs"), failed["Guide.cs"]); var rejected = await Check("round-1");
        _ = ValidateCandidate(snapshot, Snapshot(), feedback, feedback, 1);
        Json.Check(rejected.S("verdict") == "fail", "Unfixed candidate unexpectedly passed.");
        Json.Check(rejected["evidence"]!.A("findings").Any(f => f.S("ruleId") == "CSAI0002"), "First candidate must expose the recorded collection problem.");
        File.Copy(Path.Combine(output, "Guide.cs"), Path.Combine(output, "round-1.cs.txt"));
        observations.Add(Json.Create(("round", 1), ("verdict", "fail"), ("changedFileSha256", Json.Hash(failed["Guide.cs"]))));
        feedback = Json.Sha(Path.Combine(output, "round-1.json"));
        var repaired = new Dictionary<string, string>(snapshot, StringComparer.Ordinal) { ["Guide.cs"] = good };
        _ = ValidateCandidate(snapshot, repaired, feedback, feedback, 2);
        File.WriteAllText(Path.Combine(output, "Guide.cs"), good); var passed = await Check("round-2");
        _ = ValidateCandidate(snapshot, Snapshot(), feedback, feedback, 2);
        Json.Check(passed.S("verdict") == "pass" && passed["evidence"]!.B("authoritative"), "Repair did not independently pass.");
        var originalSemantic = SemanticOutcome(baseline); var repairedSemantic = SemanticOutcome(passed);
        Json.Check(JsonNode.DeepEquals(originalSemantic, repairedSemantic), "Identical restored fixture did not reproduce its defined semantic outcome.");
        var diff = await Processes.Run(["git", "diff", "--no-index", "--", "fault.cs.txt", "Guide.cs"], output, Path.Combine(output, "repair.diff"));
        Json.Check(diff.ExitCode == 1, "Expected a genuine source-only repair diff.");
        var controls = new JsonArray();
        void Denied(string id, Dictionary<string, string> candidate, string digest, int round)
        {
            try { _ = ValidateCandidate(snapshot, candidate, digest, feedback, round); throw new InvalidOperationException("Expected repair control rejection: " + id); }
            catch (InvalidDataException error) { controls.Add(Json.Create(("id", id), ("state", "Stopped"), ("reason", error.Message))); }
        }
        var forbidden = new Dictionary<string, string>(snapshot, StringComparer.Ordinal) { [".csassay.json"] = "{}" };
        Denied("protected-policy-edit", forbidden, feedback, 1); Denied("stale-feedback", repaired, new string('0', 64), 1);
        Denied("malformed-feedback", repaired, "not-a-digest", 1); Denied("budget-exhausted", repaired, feedback, 3);
        Denied("no-progress", new Dictionary<string, string>(snapshot, StringComparer.Ordinal), feedback, 1);
        observations.Add(Json.Create(("round", 2), ("verdict", "pass"), ("changedFileSha256", Json.Hash(good))));
        var result = Json.Create(("schema", "csassay-repair-replay/0.1"), ("executionStatus", "completed"),
            ("mode", "Offline registered repair replay; no model benchmark"), ("baseline", "pass"), ("fault", "fail"),
            ("rounds", observations), ("controls", controls), ("feedbackSha256", feedback), ("stopReason", "Independent verification passed within two rounds"),
            ("baselineSemanticSha256", Json.Digest(originalSemantic)), ("restoredSemanticSha256", Json.Digest(repairedSemantic)),
            ("tests", "None configured for the isolated structural specimen; ExpenseGuard business tests are a separate gate"),
            ("providerRequests", 0), ("releaseApproval", false));
        Json.Write(Path.Combine(output, "replay.json"), result); return result;
    }

    public static JsonObject SemanticOutcome(JsonNode receipt)
    {
        var e = receipt["evidence"]!;
        return Json.Create(("verdict", receipt.S("verdict")), ("exitCode", receipt.I("exitCode")), ("mode", e.S("verificationMode")), ("authority", e.B("authoritative")),
            ("findings", Json.Array(e.A("findings").Select(f => Json.Create(("ruleId", f.S("ruleId")), ("message", f.S("message")), ("scope", f.S("scope")), ("suppressed", f.B("suppressed")))))),
            ("missing", Json.Array(e.A("missingEvidence").Select(m => m.S("code")))), ("failures", Json.Array(e.A("failures").Select(f => f.S("code")))),
            ("tests", Json.Array(e.A("tests").Select(t => Json.Create(("outcome", t.S("outcome")), ("passed", t.I("passed")), ("failed", t.I("failed")), ("skipped", t.I("skipped")))))));
    }
}
