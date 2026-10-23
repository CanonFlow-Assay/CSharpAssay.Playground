namespace Assay.Tooling;

public static partial class ApplicationWorkflow
{
    public static JsonObject EvidenceIdentity(string output)
    {
        var result = new JsonObject();
        foreach (var path in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(path) is not ("run.json" or "probe-budget.json") && !path.EndsWith(".partial", StringComparison.Ordinal))
            {
                result[Relative(output, path)] = Json.Sha(path);
            }
        }

        return result;
    }
    public static async Task<JsonNode> Decide(JsonNode assessment, string output, JsonArray probes, string proposed = "", string proposalSnapshot = "", int budget = 3)
    {
        var registry = Json.Array(probes.Select(p => Json.Create(("id", p.S("id")), ("requirementId", p.S("requirementId")), ("module", p.S("module")))));
        Json.Write(Path.Combine(output, "decision-input.json"), Json.Create(("assessment", assessment), ("registry", registry), ("remainingProbes", budget), ("proposedProbe", proposed), ("proposalSnapshot", proposalSnapshot)));
        var execution = await Processes.Run(["dotnet", Decision, "decide", Path.Combine(output, "decision-input.json")], Root, Path.Combine(output, "decision.log"), 30);
        execution.Require("Typed Decision Assist");
        var value = Json.Parse(execution.Output);
        Json.Check(value.S("schema") == "assay-application-decision/0.1" && value.S("snapshotSha256") == assessment.S("snapshotSha256"), "Decision identity/schema mismatch.");
        Json.Write(Path.Combine(output, "decision.json"), value);
        return value;
    }
    public static JsonArray CoverageObservations(string root, JsonObject files, string coverage, JsonArray mutations, JsonObject previous)
    {
        root = Path.GetFullPath(root);
        var declared = files.Where(p => p.Key.EndsWith(".cs", StringComparison.Ordinal)).ToDictionary(p => Inside(root, p.Key), p => p.Key, StringComparer.Ordinal);
        var hits = new Dictionary<(string Path, string Line), int>();
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(coverage))
        {
            var tree = XDocument.Load(coverage);
            var sources = tree.Root!.Element("sources")?.Elements("source").Select(s => s.Value.Replace('\\', '/')).ToArray() ?? [];
            foreach (var cls in tree.Descendants("class"))
            {
                var filename = ((string?)cls.Attribute("filename") ?? "").Replace('\\', '/');
                var candidates = filename.StartsWith("/_/", StringComparison.Ordinal) ? [Path.GetFullPath(Path.Combine(root, filename[3..]))] : Path.IsPathRooted(filename) ? [Path.GetFullPath(filename)] : sources.Length > 0 ? sources.Where(Path.IsPathRooted).Select(s => Path.GetFullPath(Path.Combine(s, filename))).ToArray() : [Path.GetFullPath(Path.Combine(root, filename))];
                var matches = candidates.Where(declared.ContainsKey).Select(p => declared[p]).Distinct(StringComparer.Ordinal).ToArray();
                if (matches.Length != 1) { ambiguous.UnionWith(matches); continue; }
                foreach (var line in cls.Element("lines")?.Elements("line") ?? [])
                {
                    var key = (matches[0], (string?)line.Attribute("number") ?? "");
                    hits[key] = Math.Max(hits.GetValueOrDefault(key), (int?)line.Attribute("hits") ?? 0);
                }
            }
        }
        var observations = new JsonArray();
        foreach (var (name, hash) in files.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!name.EndsWith(".cs", StringComparison.Ordinal) || name.Contains("tests/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rows = ambiguous.Contains(name) ? [] : hits.Where(p => p.Key.Path == name).Select(p => p.Value).ToArray();
            observations.Add(Json.Create(("id", name), ("changed", previous.Count > 0 && previous[name]?.GetValue<string>() != hash?.GetValue<string>()), ("coverageKnown", rows.Length > 0), ("lineCoverage", rows.Length > 0 ? Math.Round(100m * rows.Count(v => v > 0) / rows.Length, 2) : 0m), ("complexity", MyRegex.Count(File.ReadAllText(Inside(root, name)))), ("survivingMutants", mutations.Count(m => m.S("path") == name && m.S("state") == "Survived"))));
        }
        return observations;
    }
    public static async Task<JsonObject> Verify(string manifestPath, string output, string? baselinePath = null, bool mutations = true)
    {
        output = Path.GetFullPath(output);
        _ = Directory.CreateDirectory(output);
        var report = Json.Create(("schema", "assay-application-run/0.1"), ("executionStatus", "running"), ("providerRequests", 0), ("authority", "C# application coordination and bounded previews. CSharpAssay retains receipt authority; ReadyForReview is not release approval."));
        Json.Write(Path.Combine(output, "run.json"), report);
        try
        {
            var spec = ApplicationRunner.Load(manifestPath);
            var root = spec.Root; var manifest = spec.Manifest; var requirements = spec.Requirements;
            var protocol = WorkerProtocol.Specification(manifest, requirements);
            var previousFiles = new JsonObject(); JsonNode? previousTests = null; JsonNode? previousContract = null;
            if (baselinePath is not null)
            {
                baselinePath = Path.GetFullPath(baselinePath);
                var old = Json.Read(baselinePath);
                Json.Check(old.S("schema") == "assay-application-run/0.1" && old.S("executionStatus") == "completed" && old.S("workflowStatus") == "ReadyForReview" && old.S("projectId") == manifest.S("id"), "A complete ready baseline from this application is required.");
                previousFiles = old["inputSha256"]!.DeepClone().AsObject();
                foreach (var name in new[] { "behavior.json", "pinned-contract.json" })
                {
                    Json.Check(Json.Sha(Path.Combine(Path.GetDirectoryName(baselinePath)!, name)) == old["evidenceSha256"].S(name), "Prior baseline artifact changed: " + name);
                }

                previousTests = Json.Read(Path.Combine(Path.GetDirectoryName(baselinePath)!, "behavior.json"));
                previousContract = Json.Read(Path.Combine(Path.GetDirectoryName(baselinePath)!, "pinned-contract.json"));
                Json.Check(manifest.I("minimumTests") >= previousTests.I("total"), "Candidate lowers the baseline test minimum.");
            }
            var manifestHash = Json.Digest(manifest);
            var files = ApplicationRunner.Snapshot(root, manifest); var source = Json.Digest(files); var requirementId = Json.Sha(Inside(root, manifest.S("requirements"))); var tools = ApplicationRunner.ToolkitIdentity();
            JsonObject? applicationArtifacts = null;
            var modules = new JsonArray();
            var intake = await ApplicationRunner.Intake(root, manifest, Path.Combine(output, "intake"));
            Json.Write(Path.Combine(output, "intake.json"), intake);
            report["projectId"] = manifest.S("id"); report["projectRoot"] = root; report["manifestPath"] = Path.GetFullPath(manifestPath); report["manifestSha256"] = manifestHash; report["inputSha256"] = files.DeepClone(); report["snapshotSha256"] = source; report["requirementsSha256"] = requirementId; report["toolkitSha256"] = tools.DeepClone();
            void Add(string module, string state, bool required, bool authoritative, string[] paths, string note)
            {
                modules.Add(Json.Create(("module", module), ("state", state), ("required", required), ("authoritative", authoritative), ("snapshotSha256", source), ("requirementsSha256", requirementId), ("artifacts", paths.Select(p => Json.Create(("path", Relative(output, p)), ("sha256", Json.Sha(p))))), ("observations", new[] { note })));
            }

            var baselineState = "NotRun";
            if (intake.S("support") == "Native")
            {
                (await Processes.Run(["dotnet", "restore", Inside(root, manifest.S("solution")), "--locked-mode"], root, Path.Combine(output, "restore.log"))).Require("Locked application restore");
                (await Processes.Run(["dotnet", "build", Inside(root, manifest.S("solution")), "--no-restore", "-c", "Release"], root, Path.Combine(output, "build.log"))).Require("Application build");
                applicationArtifacts = ApplicationRunner.ApplicationArtifacts(root, manifest);
                report["applicationArtifactsSha256"] = applicationArtifacts.DeepClone();
                var baseline = await ApplicationRunner.Tests(root, manifest, Path.Combine(output, "behavior"), coverage: true);
                baselineState = baseline.S("state"); Json.Write(Path.Combine(output, "behavior.json"), baseline);
                Json.Check(previousTests is null || previousTests.Strings("testNames").All(baseline.Strings("testNames").Contains), "Previously recorded tests disappeared.");
                Add("Behavior", baselineState, true, false, [Path.Combine(output, "behavior.json"), Path.Combine(output, "behavior/execution.log")], "Declared xUnit selection; skipped tests and execution gaps cannot pass.");
                if (baselineState == "Passed")
                {
                    var enginePath = Path.Combine(output, "engine-assessment.json");
                    File.Delete(enginePath);
                    var execution = await Processes.Run(["dotnet", Engine, "assess", Inside(root, manifest.S("solution")), "--policy", Inside(root, manifest.S("policy")), "--profile", "native", "--json", enginePath], root, Path.Combine(output, "engine.log"), 300);
                    if (File.Exists(enginePath))
                    {
                        var envelope = Json.Read(enginePath); var receipt = envelope["receipt"]!; var value = receipt["evidence"]!;
                        var authoritative = envelope["assessment"].B("authoritative") && value.B("authoritative");
                        Json.Check(envelope["assessment"].S("schema") == "csassay-assessment/0.1" && string.Equals(envelope["assessment"].S("verdict"), receipt.S("verdict"), StringComparison.OrdinalIgnoreCase), "Engine projection contradicts its receipt.");
                        var state = receipt.S("verdict") switch { "pass" => "Passed", "fail" => "Failed", "inconclusive" => "NotRun", _ => "ToolFailure" };
                        if (execution.State is ProcessState.TimedOut or ProcessState.Cancelled)
                        {
                            state = execution.StateName;
                        }

                        Json.Check(state != "Passed" || (execution.ExitCode == 0 && authoritative && value.A("missingEvidence").Count == 0 && value.A("failures").Count == 0), "Engine receipt lacks passing authority.");
                        Add("CSharpAssay", state, true, authoritative, [enginePath], value.A("findings").Count + " retained findings; underlying receipt supplies rule authority.");
                    }
                    else
                    {
                        Add("CSharpAssay", execution.State is ProcessState.TimedOut or ProcessState.Cancelled ? execution.StateName : "ToolFailure", true, false, [Path.Combine(output, "engine.log")], "No engine assessment artifact.");
                    }

                    execution.Require("Authoritative engine assessment");
                    execution = await Processes.Run(["dotnet", Inside(root, manifest.S("workerDll"))], root, Path.Combine(output, "worker.log"), 60); execution.Require("Controlled consumer execution");
                    var worker = Json.Parse(execution.Output); var observations = WorkerProtocol.Observe(worker, protocol.Worker);
                    Json.Write(Path.Combine(output, "consumer.json"), worker);
                    Add("ApplicationChecks", observations.S("state"), true, false, [Path.Combine(output, "consumer.json")], string.Join(",", observations.Strings("failedObservations")));
                    var traceability = WorkerProtocol.TraceRequirements(requirements, baseline, observations, manifest.S("schema") == "assay-project/0.2"); Json.Write(Path.Combine(output, "traceability.json"), traceability);
                    Add("RequirementsAssay", traceability.S("state"), true, false, [Path.Combine(output, "traceability.json")], "Explicit mappings to named executions; completeness remains a review obligation.");
                    var pinned = previousContract ?? Json.Read(Inside(root, manifest.S("contractBaseline"))); Json.Write(Path.Combine(output, "pinned-contract.json"), pinned); report["pinnedContractSha256"] = Json.Sha(Path.Combine(output, "pinned-contract.json"));
                    var mutationReport = mutations ? await ApplicationRunner.Mutations(root, manifest, Path.Combine(output, "mutation"), baseline.I("total")) : Json.Create(("executionStatus", "not-requested"), ("results", new JsonArray()), ("unexpectedOutcomes", new JsonArray()));
                    Json.Write(Path.Combine(output, "mutation.json"), mutationReport);
                    var rows = mutationReport.A("results"); var mutationState = !mutations ? "NotRun" : mutationReport.A("unexpectedOutcomes").Count > 0 ? "Failed" : rows.Any(r => r.S("state") == "Survived") ? "Survived" : "Passed";
                    var incomplete = rows.FirstOrDefault(r => r.S("state") is "Invalid" or "TimedOut" or "Cancelled" or "ToolFailure"); if (incomplete is not null)
                    {
                        mutationState = incomplete.S("state");
                    }

                    Add("MutationAssay", mutationState, false, false, [Path.Combine(output, "mutation.json")], "All survivors retained; preview is advisory.");
                    Json.Write(Path.Combine(output, "module-input.json"), Json.Create(("baselineContract", pinned), ("candidateContract", worker["observedContract"]), ("risk", CoverageObservations(root, files, Path.Combine(output, "behavior/coverage.cobertura.xml"), rows, previousFiles)), ("trace", observations["trace"]), ("traceContract", protocol.Worker["traceContract"])));
                    execution = await Processes.Run(["dotnet", Decision, "modules", Path.Combine(output, "module-input.json")], Root, Path.Combine(output, "modules.log"), 30); execution.Require("Preview modules");
                    var values = Json.Parse(execution.Output); Json.Write(Path.Combine(output, "modules.json"), values);
                    Add("ContractAssay", values.A("contractDifferences").Count > 0 ? "Failed" : "Passed", true, false, [Path.Combine(output, "modules.json"), Path.Combine(output, "pinned-contract.json")], "Selected public members and outcome comparison; not universal serialization compatibility.");
                    Add("ResilienceAssay", values.A("resilienceIssues").Count > 0 ? "Failed" : "Passed", true, false, [Path.Combine(output, "consumer.json"), Path.Combine(output, "modules.json")], "Declared scenario/check identities and bounded trace contract.");
                    Add("CodeRiskAssay", "Passed", false, false, [Path.Combine(output, "modules.json")], "Ordinal heuristic, lexical branches, unknown coverage retained.");
                }
            }
            var assessment = Json.Create(("schema", "assay-application-assessment/0.1"), ("snapshotSha256", source), ("requirementsSha256", requirementId), ("support", intake.S("support")), ("baseline", baselineState), ("requirements", requirements.A("requirements").Select(r => Json.Create(("id", r.S("id")), ("description", r.S("description")), ("probeIds", r!["probeIds"])))), ("modules", modules));
            var advice = await Decide(assessment, output, protocol.Probes);
            Json.Check(Json.Digest(Json.Read(manifestPath)) == manifestHash && Json.Digest(ApplicationRunner.Snapshot(root, manifest)) == source && JsonNode.DeepEquals(ApplicationRunner.ToolkitIdentity(), tools), "Source/configuration/toolkit changed while executing.");
            Json.Check(applicationArtifacts is null || JsonNode.DeepEquals(ApplicationRunner.ApplicationArtifacts(root, manifest), applicationArtifacts), "Compiled application artifacts changed while executing.");
            report["executionStatus"] = "completed"; report["assessment"] = assessment; report["workflowStatus"] = advice.S("workflowStatus"); report["decision"] = advice["decision"]!.DeepClone(); report["evidenceSha256"] = EvidenceIdentity(output); report["reviewStatus"] = "Agent qualification; independent review remains separate.";
            Json.Write(Path.Combine(output, "run.json"), report); Json.Write(Path.Combine(output, "probe-budget.json"), Json.Create(("snapshotSha256", source), ("remaining", 3)));
            return report;
        }
        catch (Exception failure)
        {
            report["executionStatus"] = failure is OperationCanceledException ? "cancelled" : "failed"; report["failure"] = failure.Message; Json.Write(Path.Combine(output, "run.json"), report); throw;
        }
    }

    private static readonly string[] values = new[] { "Establish passing baseline", "Preserve pinned contract unless explicitly reviewed", "Implement native outcomes and effect boundaries", "Add tests against stated criteria", "Verify against previous baseline receipt", "Retain failures, advisories and survivors" };

    public static JsonObject Plan(string manifestPath, string requirementPath, string output)
    {
        var spec = ApplicationRunner.Load(manifestPath); var files = ApplicationRunner.Snapshot(spec.Root, spec.Manifest); var request = Json.Read(requirementPath);
        Json.Check(request.S("schema") == "assay-change-request/0.1" && !string.IsNullOrWhiteSpace(request.S("description")) && request.A("acceptanceCriteria").Count > 0, "Change request needs a description and explicit acceptance criteria.");
        var result = Json.Create(("schema", "assay-coding-work-item/0.1"), ("snapshotSha256", Json.Digest(files)), ("inputSha256", files), ("requirementsSha256", Json.Sha(Inside(spec.Root, spec.Manifest.S("requirements")))), ("requestSha256", Json.Sha(requirementPath)), ("request", request), ("status", "implementation-required"), ("allowedInvestigationIds", spec.Requirements.A("requirements").SelectMany(r => r.Strings("probeIds")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)), ("obligations", values), ("authority", "Coding handoff only; no claim of implementation or verification."));
        Json.Write(output, result); return result;
    }
    public static async Task<JsonObject> Investigate(string manifestPath, string runPath, string probeId, string output)
    {
        var spec = ApplicationRunner.Load(manifestPath); var previous = Json.Read(runPath);
        Json.Check(previous.S("executionStatus") == "completed" && previous.S("projectId") == spec.Manifest.S("id"), "Completed evidence from this application is required.");
        Json.Check(Json.Digest(spec.Manifest) == previous.S("manifestSha256"), "Application manifest changed or is unpinned; reassess first.");
        Json.Check(previous["applicationArtifactsSha256"] is JsonObject expectedArtifacts && expectedArtifacts.Count > 0 && JsonNode.DeepEquals(ApplicationRunner.ApplicationArtifacts(spec.Root, spec.Manifest), expectedArtifacts), "Compiled application artifacts changed or are unpinned; reassess first.");
        var identity = Json.Digest(ApplicationRunner.Snapshot(spec.Root, spec.Manifest)); var tools = ApplicationRunner.ToolkitIdentity();
        Json.Check(identity == previous.S("snapshotSha256") && JsonNode.DeepEquals(tools, previous["toolkitSha256"]), "Decision is stale after source/configuration/toolkit changes; reassess first.");
        var parent = Path.GetDirectoryName(Path.GetFullPath(runPath))!;
        foreach (var (name, expected) in previous["evidenceSha256"]!.AsObject())
        {
            Json.Check(Json.Sha(Inside(parent, name)) == expected!.GetValue<string>(), "Prior evidence changed: " + name);
        }

        var protocol = WorkerProtocol.Specification(spec.Manifest, spec.Requirements);
        var selected = protocol.Probes.SingleOrDefault(p => p.S("id") == probeId) ?? throw new InvalidDataException("Unregistered investigation identity.");
        // FileShare.None gives a portable exclusive cross-process budget lock.
        using (var stream = new FileStream(Path.Combine(parent, "probe-budget.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            var budget = Json.Parse(await reader.ReadToEndAsync());
            Json.Check(budget.S("snapshotSha256") == identity && budget.I("remaining") is > 0 and <= 3, "Investigation budget exhausted or stale.");
            var validated = await Decide(previous["assessment"]!, Path.Combine(output, "selection"), protocol.Probes, probeId, identity, budget.I("remaining"));
            Json.Check(validated["decision"].S("action") == "RunRegisteredProbe" && validated["decision"].S("probeId") == probeId, "Investigation is not eligible for this assessment.");
            budget["remaining"] = budget.I("remaining") - 1; stream.Position = 0; stream.SetLength(0); await stream.WriteAsync(Encoding.UTF8.GetBytes(budget.ToJsonString())); await stream.FlushAsync();
        }
        JsonObject result;
        if (selected.S("kind") == "tests")
        {
            result = await ApplicationRunner.Tests(spec.Root, spec.Manifest, Path.Combine(output, "probe"));
        }
        else
        {
            var execution = await Processes.Run(["dotnet", Inside(spec.Root, spec.Manifest.S("workerDll"))], spec.Root, Path.Combine(output, "probe.log"), 60); execution.Require("Registered worker probe");
            var worker = Json.Parse(execution.Output); var observed = WorkerProtocol.Observe(worker, protocol.Worker);
            Json.Write(Path.Combine(output, "probe-input.json"), Json.Create(("baselineContract", Json.Read(Path.Combine(parent, "pinned-contract.json"))), ("candidateContract", observed["observedContract"]), ("risk", new JsonArray()), ("trace", observed["trace"]), ("traceContract", protocol.Worker["traceContract"])));
            execution = await Processes.Run(["dotnet", Decision, "modules", Path.Combine(output, "probe-input.json")], Root, Path.Combine(output, "probe-modules.log"), 30); execution.Require("Registered module comparison");
            var computed = Json.Parse(execution.Output); var issues = computed.A(selected.S("module") == "ContractAssay" ? "contractDifferences" : "resilienceIssues");
            result = Json.Create(("state", issues.Count > 0 || observed.S("state") == "Failed" ? "Failed" : "Passed"), ("module", selected.S("module")), ("issues", issues), ("observations", observed));
        }
        Json.Check(Json.Digest(Json.Read(manifestPath)) == previous.S("manifestSha256") && Json.Digest(ApplicationRunner.Snapshot(spec.Root, spec.Manifest)) == identity && JsonNode.DeepEquals(ApplicationRunner.ToolkitIdentity(), tools) && JsonNode.DeepEquals(ApplicationRunner.ApplicationArtifacts(spec.Root, spec.Manifest), previous["applicationArtifactsSha256"]), "Inputs changed during investigation.");
        result["probeId"] = probeId; result["snapshotSha256"] = identity; result["executionStatus"] = "completed";
        Json.Write(Path.Combine(output, "probe-result.json"), result); return result;
    }

    [GeneratedRegex("\\b(if|case|catch|while|for|foreach)\\b")]
    private static partial Regex MyRegex { get; }
}
