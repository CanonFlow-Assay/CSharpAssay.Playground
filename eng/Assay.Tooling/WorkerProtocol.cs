namespace Assay.Tooling;

public sealed record WorkerSpec(JsonNode Worker, JsonArray Probes);
public static class WorkerProtocol
{
    public static WorkerSpec Specification(JsonNode manifest, JsonNode requirements)
    {
        if (manifest.S("schema") == "assay-project/0.1" && manifest["worker"] is null)
        {
            return Legacy(requirements);
        }

        var worker = manifest["worker"] ?? throw new InvalidDataException("Missing worker specification.");
        Json.Check(worker.S("schema") == "assay-worker-execution/0.1", "Unsupported worker schema.");
        var keys = worker.Strings("scenarioKeys");
        var checks = worker.Strings("checkIds");
        Json.Check(keys.Length > 0 && checks.Length > 0 && keys.Concat(checks).All(s => s.Length > 0) && keys.Distinct(StringComparer.Ordinal).Count() == keys.Length && checks.Distinct(StringComparer.Ordinal).Count() == checks.Length, "Declare unique nonempty worker observation identities.");
        var variants = worker.Strings("traceVariants");
        Json.Check(variants.Length > 0 && variants.All(v => keys.Any(k => k.Split('/')[^1] == v)), "Trace selection must identify declared worker variants.");
        var contract = worker["traceContract"]?.AsObject() ?? throw new InvalidDataException("Missing trace contract.");
        Json.Check(contract.Select(p => p.Key).ToHashSet(StringComparer.Ordinal).SetEquals(["maximumAttempts", "deadlineTicks", "allowsFallback", "atMostOneSideEffect"]), "Declare the complete trace contract.");
        try { Json.Check(contract.I("maximumAttempts") > 0 && contract.L("deadlineTicks") > 0, "Invalid trace limits."); _ = contract.B("allowsFallback"); _ = contract.B("atMostOneSideEffect"); }
        catch (InvalidOperationException) { throw new InvalidDataException("Trace limits and flags require exact integer/Boolean types."); }
        var probes = manifest.A("probes");
        Json.Check(probes.Count > 0 && probes.Select(p => p.S("id")).Distinct(StringComparer.Ordinal).Count() == probes.Count, "Unique registered probes are required.");
        var declared = requirements.A("requirements").ToDictionary(r => r.S("id"), r => r.Strings("probeIds"), StringComparer.Ordinal);
        foreach (var probe in probes)
        {
            Json.Check(probe!.AsObject().Select(p => p.Key).ToHashSet(StringComparer.Ordinal).SetEquals(["id", "requirementId", "module", "kind"]), "Arbitrary probe command fields are forbidden.");
            Json.Check(declared.TryGetValue(probe.S("requirementId"), out var ids) && ids.Contains(probe.S("id"), StringComparer.Ordinal), "Probe is not bound to a declared requirement.");
            Json.Check((probe.S("kind"), probe.S("module")) is ("tests", "Behavior") or ("worker", "ContractAssay") or ("worker", "ResilienceAssay"), "Probe kind cannot reproduce its registered module.");
        }
        Json.Check(requirements.A("requirements").All(r => r.Strings("probeIds").All(id => probes.Any(p => p.S("id") == id && p.S("requirementId") == r.S("id")))), "A requirement refers to an absent or differently bound probe.");
        return new(worker, probes);
    }

    private static readonly string[] values = new[] { "breaker-open-rejects", "breaker-recovered", "concurrent-recovery-rejected", "concurrency-limit-rejects", "concurrency-permit-released", "fallback-marked-degraded" };
    private static readonly string[] valuesArray = new[] { "functional" };

    private static WorkerSpec Legacy(JsonNode requirements)
    {
        string[] names = ["success", "retry-recovery", "permanent", "exhausted", "deadline", "total-deadline", "cancel-before", "cancel-during"];
        var worker = Json.Create(("schema", "polly-consumer-execution/0.1"), ("legacy", true), ("scenarioKeys", names.SelectMany(n => new[] { n + "/original", n + "/functional" })), ("checkIds", values), ("traceVariants", valuesArray), ("traceContract", Json.Create(("maximumAttempts", 3), ("deadlineTicks", 20000000), ("allowsFallback", false), ("atMostOneSideEffect", true))));
        var modules = new Dictionary<string, string>(StringComparer.Ordinal) { ["behavior"] = "Behavior", ["contract"] = "ContractAssay", ["resilience"] = "ResilienceAssay" };
        var probes = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var requirement in requirements.A("requirements"))
        {
            foreach (var id in requirement.Strings("probeIds"))
            {
                if (modules.TryGetValue(id, out var module))
                {
                    probes[id] = Json.Create(("id", id), ("requirementId", requirement.S("id")), ("module", module), ("kind", id == "behavior" ? "tests" : "worker"));
                }
            }
        }

        return new(worker, Json.Array(probes.Values));
    }
    public static JsonObject Observe(JsonNode value, JsonNode worker)
    {
        Json.Check(value.S("schema") == worker.S("schema") && value.S("executionStatus") == "completed", "Worker schema/completion mismatch.");
        var legacy = worker.B("legacy");
        var scenarios = value.A("scenarios");
        var checks = value.A("infrastructureChecks");
        var keys = scenarios.Select(s => s.S("id") + "/" + s.S(legacy ? "pipeline" : "variant")).ToArray();
        var checkIds = legacy ? checks.Select(c => c!.GetValue<string>()).ToArray() : checks.Select(c => c.S("id")).ToArray();
        Json.Check(keys.Distinct(StringComparer.Ordinal).Count() == keys.Length && keys.ToHashSet(StringComparer.Ordinal).SetEquals(worker.Strings("scenarioKeys")) && checkIds.Distinct(StringComparer.Ordinal).Count() == checkIds.Length && checkIds.ToHashSet(StringComparer.Ordinal).SetEquals(worker.Strings("checkIds")), "Worker observations differ from declared identity sets.");
        var failed = new List<string>();
        if (!legacy)
        {
            foreach (var observation in scenarios.Concat(checks))
            {
                Json.Check(observation?["passed"] is JsonValue boolean && boolean.TryGetValue<bool>(out _), "Every worker outcome requires an explicit Boolean.");
                if (!observation.B("passed"))
                {
                    failed.Add(observation.S("id"));
                }
            }
        }

        var selected = scenarios.Where(s => worker.Strings("traceVariants").Contains(s.S(legacy ? "pipeline" : "variant"), StringComparer.Ordinal)).ToArray();
        Json.Check(selected.Length > 0 && selected.All(s => s.A("trace").Count > 0), "Every selected worker scenario requires its own trace.");
        var traces = Json.Array(selected.SelectMany(s => s.A("trace")));
        Json.Check(traces.Count > 0 && value["observedContract"] is not null, "Missing worker trace or contract observations.");
        return Json.Create(("state", failed.Count > 0 ? "Failed" : "Passed"), ("failedObservations", failed), ("scenarioKeys", keys), ("trace", traces), ("observedContract", value["observedContract"]));
    }
    public static JsonObject TraceRequirements(JsonNode requirements, JsonNode behavior, JsonNode observations, bool required)
    {
        var names = behavior.Strings("testNames").ToHashSet(StringComparer.Ordinal);
        var scenarios = observations.Strings("scenarioKeys").ToHashSet(StringComparer.Ordinal);
        var rows = new JsonArray();
        foreach (var requirement in requirements.A("requirements"))
        {
            var criteria = requirement.A("criteria");
            if (criteria.Count == 0 && required)
            {
                rows.Add(Json.Create(("requirementId", requirement.S("id")), ("criterionId", ""), ("state", "Failed"), ("reason", "Missing explicit acceptance criteria.")));
            }

            Json.Check(criteria.Select(c => c.S("id")).Distinct(StringComparer.Ordinal).Count() == criteria.Count, "Duplicate criterion identity.");
            foreach (var criterion in criteria)
            {
                var tests = criterion.Strings("testNames");
                var checks = criterion.Strings("scenarioKeys");
                var passed = !string.IsNullOrWhiteSpace(criterion.S("text")) && tests.Length > 0 && tests.All(names.Contains) && checks.All(scenarios.Contains) && behavior.S("state") == "Passed" && observations.S("state") == "Passed";
                rows.Add(Json.Create(("requirementId", requirement.S("id")), ("criterionId", criterion.S("id")), ("testNames", tests), ("scenarioKeys", checks), ("state", passed ? "Passed" : "Failed")));
            }
        }
        return Json.Create(("schema", "assay-requirement-evidence/0.1"), ("state", rows.Any(r => r.S("state") == "Failed") ? "Failed" : "Passed"), ("criteria", rows), ("scope", "Declared mappings to executed named tests and worker observations; not semantic proof of completeness."));
    }
}
