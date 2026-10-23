namespace Assay.Tooling;

public static class AdviceEvaluation
{
    public static readonly ImmutableDictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.Ordinal) { ["openai"] = "OPENAI_API_KEY", ["jev"] = "TYPESAFE_API_KEY", ["clef"] = "CLOUDFLARE_AUTH_TOKEN", ["clef-flash"] = "CLOUDFLARE_AUTH_TOKEN" }.ToImmutableDictionary(StringComparer.Ordinal);
    public static void ValidateRequest(bool live, string[] providers, int maximum, int eligible, int limit = 32)
    {
        Json.Check(providers.Distinct(StringComparer.Ordinal).Count() == providers.Length && providers.Length <= 3 && maximum >= 1 && maximum <= limit, "Select distinct providers and a valid request budget.");
        Json.Check(live || providers.All(p => p == "none"), "Explicit --live is required before provider calls.");
        Json.Check(eligible <= maximum, "Selected comparison exceeds request budget; no live calls made.");
        Json.Check(providers.All(p => p is "none" or "jev" or "openai" or "clef" or "clef-flash" or "clef-local" or "laya" or "qwen" or "chat" or "setfit" or "custom"), "Unregistered provider identity.");
    }
    public static void ValidateConfiguration(string[] providers, Func<string, bool> configured)
    {
        foreach (var provider in providers)
        {
            if (Keys.TryGetValue(provider, out var variable))
            {
                Json.Check(configured(variable), variable + " must be configured; no calls made.");
            }
        }
    }
    public static async Task<JsonObject> Shape(Arguments args)
    {
        var providers = args.All("--provider"); var maximum = args.Count("--max-requests", 8); ValidateRequest(args.Has("--live"), providers, maximum, 0, 16);
        var available = providers.Where(p => Keys.TryGetValue(p, out var key) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key))).ToArray();
        var directory = Path.Combine(Root, SampleInvestigations.Shape.Output); var investigation = Json.Read(Path.Combine(directory, "investigations.json"));
        Json.Check(investigation.S("executionStatus") == "completed", "Complete Shape investigations required."); SampleInvestigations.AssertBindings(investigation["inputSha256"]!);
        var before = investigation.A("results").Where(r => r!["beforeAdvice"].S("adviceStatus") == "suggested").Take(3).Select(r => Path.Combine(directory, "cases", r.S("id"), "before/experiment.json"));
        var after = investigation.A("results").Where(r => r!["afterAdvice"].S("adviceStatus") == "suggested").Take(1).Select(r => Path.Combine(directory, "cases", r.S("id"), "after/experiment.json")); var packets = before.Concat(after).ToArray();
        ValidateRequest(args.Has("--live"), providers, maximum, packets.Length * available.Length, 16);
        var report = Json.Create(("schema", "shape-advice-comparison/0.1"), ("maxRequests", maximum), ("requestsAttempted", 0), ("results", new JsonArray()), ("providers", providers.ToDictionary(p => p, p => available.Contains(p, StringComparer.Ordinal) ? "configured" : "unavailable_missing_key", StringComparer.Ordinal)), ("qualityGateApplied", false)); var path = Path.Combine(directory, "provider-comparison.json"); Json.Write(path, report); var stopped = false;
        foreach (var packet in packets)
        {
            var baseline = await Invoke(packet, "none"); Json.Check(baseline.S("executionStatus") == "completed" && baseline["advice"].S("adviceStatus") == "suggested", "Ineligible provider comparison packet.");
            foreach (var provider in available)
            {
                if (stopped) { report.A("results").Add(Json.Create(("provider", provider), ("status", "not_run_after_failure"))); continue; }
                report["requestsAttempted"] = report.I("requestsAttempted") + 1; Json.Write(path, report); var result = await Invoke(packet, provider); report.A("results").Add(Json.Create(("packet", Relative(directory, packet)), ("provider", provider), ("packetSha256", Json.Sha(packet)), ("baseline", baseline), ("observation", result)));
                stopped = result.S("executionStatus") != "completed" || result["advice"].S("verificationVerdict") != "Inconclusive" || result["advice"].S("adviceStatus") == "unavailable"; Json.Write(path, report);
            }
        }
        report["executionStatus"] = stopped ? "stopped_after_failure" : available.Length > 0 ? "completed" : "unavailable_missing_keys"; Json.Write(path, report); return report;
    }
    private static async Task<JsonObject> Invoke(string path, string provider)
    {
        var watch = Stopwatch.StartNew(); var execution = await Processes.Run(["dotnet", Advice, path, "--provider=" + provider], Root, seconds: 40);
        if (execution.State != ProcessState.Passed)
        {
            return Json.Create(("executionStatus", "cli_failed"), ("exitCode", execution.ExitCode));
        }

        var value = Json.Parse(execution.Output); Json.Check(value.S("schema") == "playground-decision-advice/0.1", "Unexpected advice schema.");
        return Json.Create(("executionStatus", "completed"), ("elapsedSeconds", Math.Round(watch.Elapsed.TotalSeconds, 3)), ("advice", value));
    }
    public static async Task<JsonObject> Scenarios(Arguments args)
    {
        var providers = args.All("--provider"); if (providers.Length == 0)
        {
            providers = ["none"];
        }

        var maximum = args.Count("--max-requests", 8); ValidateRequest(args.Has("--live"), providers, maximum, 0);
        ValidateConfiguration(providers, variable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)));
        var output = Path.Combine(Root, "evidence/generated/decision-assistance"); var basePacket = Json.Read(Path.Combine(output, "experiment.json")); var scenarioPath = Path.Combine(Root, "samples/50-decision-assistance/corpus/scenarios.json"); var corpus = Json.Read(scenarioPath);
        Json.Check(corpus.S("schema") == "playground-advice-scenarios/0.1" && corpus.B("synthetic"), "Unsupported scenario set.");
        var report = Json.Create(("schema", "playground-advice-evaluation/0.1"), ("synthetic", true), ("hypotheticalStates", true), ("labelReviewStatus", corpus["labelReviewStatus"]), ("qualityGateApplied", false), ("scenarioSetSha256", Json.Sha(scenarioPath)), ("cliSha256", Json.Sha(Advice)), ("sourceSha256", basePacket.S("sourceSha256")), ("providers", providers), ("maxProviderRequests", maximum), ("providerRequestsEligible", 0), ("providerRequestsAttempted", 0), ("results", new JsonArray()), ("authority", "Hypothetical scenario evaluation, not human-adjudicated provider qualification."));
        var reportPath = Path.Combine(output, "evaluation-" + string.Join('-', providers) + ".json");
        using var scratch = new TemporaryWorkspace("assay-scenarios-");
        var prepared = new List<(JsonNode Case, string Path, JsonObject Baseline, bool Eligible)>();
        foreach (var item in corpus.A("cases"))
        {
            var path = Inside(scratch.Path, item.S("id") + ".json"); var packet = Json.Create(("schema", "playground-mutation-experiment/0.1"), ("synthetic", true), ("sourceSha256", basePacket.S("sourceSha256")), ("hypotheticalState", true), ("scenarioId", item.S("id")));
            foreach (var (key, value) in item!["observations"]!.AsObject())
            {
                packet[key] = value?.DeepClone();
            }

            Json.Write(path, packet);
            var baseline = await Invoke(path, "none"); Json.Check(baseline.S("executionStatus") == "completed" && baseline["advice"].S("action") == item.S("expectedAction") && baseline["advice"].S("adviceStatus") == item.S("expectedStatus") && baseline["advice"].S("verificationVerdict") == "Inconclusive", "Offline expectations failed; no provider calls made.");
            var eligible = baseline["advice"].S("adviceStatus") == "suggested"; if (eligible)
            {
                report["providerRequestsEligible"] = report.I("providerRequestsEligible") + providers.Count(p => p != "none");
            }

            prepared.Add((item, path, baseline, eligible));
        }
        ValidateRequest(args.Has("--live"), providers, maximum, report.I("providerRequestsEligible")); var stopped = false;
        foreach (var (item, path, baseline, eligible) in prepared)
        {
            var row = Json.Create(("id", item.S("id")), ("scenarioInputSha256", Json.Sha(path)), ("expectedAction", item.S("expectedAction")), ("expectedStatus", item.S("expectedStatus")), ("reason", item.S("reason")), ("baselineMatches", true), ("baseline", baseline), ("providerEligible", eligible), ("observations", new JsonArray()));
            foreach (var provider in providers)
            {
                if (stopped) { row.A("observations").Add(Json.Create(("provider", provider), ("executionStatus", "not_run_after_failure"))); continue; }
                // Ineligible evidence never reaches a provider, even if credentials are present.
                JsonObject result;
                if (provider == "none" || !eligible)
                {
                    result = baseline.DeepClone().AsObject();
                }
                else { report["providerRequestsAttempted"] = report.I("providerRequestsAttempted") + 1; Json.Write(reportPath, report); result = await Invoke(path, provider); }
                result["provider"] = provider;
                if (result.S("executionStatus") == "completed") { var advice = result["advice"]!; result["matchesDraftExpectation"] = advice.S("action") == item.S("expectedAction") && advice.S("adviceStatus") == item.S("expectedStatus"); result["verdictUnchanged"] = advice.S("verificationVerdict") == "Inconclusive"; stopped = !result.B("verdictUnchanged") || advice.S("adviceStatus") == "unavailable"; }
                else
                {
                    stopped = true;
                }

                row.A("observations").Add(result);
            }
            report.A("results").Add(row); Json.Write(reportPath, report);
        }
        report["executionStatus"] = stopped ? "stopped_after_failure" : "completed"; Json.Write(reportPath, report); return report;
    }
    public static async Task<JsonObject> Csv(Arguments args)
    {
        var providers = args.All("--provider"); var maximum = args.Count("--max-requests", 6); var configured = providers.ToDictionary(p => p, p => Keys.TryGetValue(p, out var variable) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)), StringComparer.Ordinal);
        ValidateRequest(args.Has("--live"), providers, maximum, 3 * configured.Count(p => p.Value), 6);
        var directory = Path.Combine(Root, "evidence/generated/csvhelper"); var path = Path.Combine(directory, "advice-comparison.json"); var investigationPath = Path.Combine(directory, "functional/investigations.json"); var investigation = Json.Read(investigationPath); Json.Check(investigation.S("executionStatus") == "completed", "Complete source-bound investigations required."); SampleInvestigations.AssertBindings(investigation["inputSha256"]!);
        var report = Json.Create(("schema", "csv-advice-comparison/0.1"), ("executionStatus", "running"), ("maxRequests", maximum), ("requestsAttempted", 0), ("results", new JsonArray()), ("liveRequested", args.Has("--live")), ("providers", configured.ToDictionary(p => p.Key, p => p.Value ? "configured" : "unavailable_missing_key", StringComparer.Ordinal)), ("investigationsSha256", Json.Sha(investigationPath)), ("scriptSha256", Json.Sha(Path.Combine(Root, "eng/Assay.Tooling/AdviceEvaluation.cs"))), ("authority", "Agent-labelled action agreement, not human usefulness or provider qualification.")); Json.Write(path, report);
        (string Id, string Phase, string Action)[] cases = [("whole-min-boundary", "before", "InspectAssertion"), ("alias-trim", "before", "InspectReachability"), ("flag-equivalent-pattern", "after", "ReviewContract")];
        var packets = new List<(string Path, string Action)>();
        foreach (var (id, phase, expected) in cases) { var packet = Path.Combine(directory, "functional/mutations", id, phase, "experiment.json"); var baseline = await SampleInvestigations.AdviceFor(packet); Json.Check(baseline.S("action") == expected && baseline.S("adviceStatus") == "suggested", "CSV case is not eligible with declared expectation."); packets.Add((packet, expected)); }
        var stopped = false;
        foreach (var (packet, expected) in packets)
        {
            foreach (var (provider, ready) in configured)
            {
                if (!ready || stopped)
                {
                    continue;
                }

                report["requestsAttempted"] = report.I("requestsAttempted") + 1; Json.Write(path, report);
                var result = await Invoke(packet, provider); Json.Check(result.S("executionStatus") == "completed" && result["advice"].S("verificationVerdict") == "Inconclusive", "Provider failed or exceeded authority; no fallback/retry.");
                report.A("results").Add(Json.Create(("case", Relative(Path.Combine(directory, "functional"), packet)), ("packetSha256", Json.Sha(packet)), ("provider", provider), ("advice", result["advice"]), ("agentExpectedAction", expected), ("matchesAgentExpectation", result["advice"].S("action") == expected)));
                stopped = result["advice"].S("adviceStatus") == "unavailable"; Json.Write(path, report);
            }
        }

        report["executionStatus"] = stopped ? "stopped_after_failure" : configured.Any(p => p.Value) ? "completed" : args.Has("--live") ? "unavailable_missing_keys" : "not_requested";
        report["selectedCases"] = Json.Array(cases.Select(c => Json.Create(("id", c.Id), ("phase", c.Phase), ("agentExpectedAction", c.Action)))); Json.Write(path, report); return report;
    }
}
