namespace Assay.Tooling;

public static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("Supply an Assay tooling command; see eng/tooling help.");
            }

            var command = args[0]; var options = new Arguments(args[1..]); JsonNode? result;
            if (command == "test-process") { await Task.Delay(options.Count("--delay", 0)); return options.Count("--exit", 0); }
            if (command == "help") { Console.WriteLine("C# Assay tooling: daa new|intake|verify|plan|investigate; sdk-source; audit-sample-toolchain; run-decision-assistance; evaluate-decision-assistance; run-shape-investigations; run-csvhelper-functional|streaming|hefty|roundtrip; evaluate-csvhelper-advice; prepare-csvhelper-review; qualify-application-workflow; qualify-inventory-maturity; assert-evidence|gof-crosswalk|functional-shape|csvhelper; with-inventory-database -- command; test-all; self-audit."); return 0; }
            if (command == "read-sdk") { Console.WriteLine(Sdk); return 0; }
            if (command == "sdk-source") { Console.WriteLine(await PackageTools.SdkSource()); return 0; }
            if (command == "normalize-preview-package") { foreach (var path in options.Positionals) { PackageTools.Normalize(path); } return 0; }
            if (command == "copy-qualified") { Json.Check(options.Positionals.Count == 2, "Supply engine root and destination."); await PackageTools.CopyQualified(options.Positionals[0], options.Positionals[1]); Console.WriteLine("Copied exact qualified packages without rebuilding engine."); return 0; }
            if (command == "with-inventory-database")
            {
                return await LocalServices.WithDatabase(options.Positionals.ToArray());
            }

            if (command == "serve-local-advice") { await LocalServices.Serve(options); return 0; }
            if (command == "serve-docs") { await LocalServices.ServeDocs(options.Count("--port", 8080)); return 0; }
            if (command.StartsWith("test-", StringComparison.Ordinal))
            {
                return await RunTests(command);
            }

            if (command == "daa")
            {
                var action = options.Positionals.FirstOrDefault() ?? throw new ArgumentException("Supply new|intake|verify|plan|investigate.");
                if (action == "new") { result = await Scaffolding.Create(options.Required("--output"), options.Required("--name")); Console.WriteLine(result.ToJsonString(Json.Options)); return 0; }
                var path = options.Required("--manifest"); var spec = ApplicationRunner.Load(path); var output = Path.GetFullPath(options.Get("--output", Path.Combine(spec.Root, "evidence/generated/application-assessment")));
                result = action switch
                {
                    "intake" => await ApplicationRunner.Intake(spec.Root, spec.Manifest, Path.Combine(output, "intake")),
                    "verify" => await ApplicationWorkflow.Verify(path, output, options.Get("--baseline"), !options.Has("--skip-mutations")),
                    "plan" => ApplicationWorkflow.Plan(path, options.Required("--requirement"), Path.Combine(output, "work-item.json")),
                    "investigate" => await ApplicationWorkflow.Investigate(path, options.Required("--run"), options.Required("--probe"), output),
                    _ => throw new ArgumentException("Unknown DAA action.")
                };
                if (action == "intake")
                {
                    Json.Write(Path.Combine(output, "intake.json"), result);
                }

                Console.WriteLine(result.ToJsonString(Json.Options)); return action == "verify" && result.S("workflowStatus") != "ReadyForReview" ? 2 : 0;
            }
            switch (command)
            {
                case "assert-evidence": EvidenceAssertions.Primary(); return 0;
                case "assert-gof-crosswalk": EvidenceAssertions.Gof(); return 0;
                case "assert-functional-shape": EvidenceAssertions.Shape(); return 0;
                case "assert-eshop-evidence": ArchivedAssessments.ShopEvidence(); return 0;
                case "assert-eshop-ordering": await ArchivedAssessments.ShopOrdering(options.Positionals.Single()); return 0;
                case "assert-eshop-candidate": await ArchivedAssessments.ShopCandidate(options.Positionals.Single()); return 0;
                case "self-audit": result = await SelfAudit(); break;
                case "audit-sample-toolchain": result = await PackageTools.AuditToolchain(Path.GetFullPath(options.Get("--output", Path.Combine(Root, "evidence/generated/all-samples/toolchain.json")))); break;
                case "run-decision-assistance": result = await DecisionExperiments.RunCorpus(); break;
                case "evaluate-decision-assistance": result = await AdviceEvaluation.Scenarios(options); break;
                case "run-shape-investigations":
                    AdviceEvaluation.ValidateRequest(options.Has("--live"), options.All("--provider"), options.Count("--max-requests", 8), 0, 16);
                    result = await SampleInvestigations.Run(SampleInvestigations.Shape);
                    if (options.All("--provider").Length > 0)
                    {
                        result["providerComparison"] = await AdviceEvaluation.Shape(options);
                    }

                    break;
                case "run-csvhelper-functional": result = await SampleInvestigations.Run(SampleInvestigations.Csv); break;
                case "run-csvhelper-streaming": result = await Benchmarks.Streaming(); break;
                case "run-csvhelper-hefty": result = await Benchmarks.Hefty(options.Has("--reuse-fixtures")); break;
                case "run-csvhelper-roundtrip": result = await Benchmarks.Roundtrip(); break;
                case "evaluate-csvhelper-advice": result = await AdviceEvaluation.Csv(options); break;
                case "prepare-csvhelper-review": result = HumanReview.Prepare(options.Get("--responses")); break;
                case "assert-csvhelper": result = EvidenceAssertions.Csv(); break;
                case "assess-csvhelper-upstream": result = await ArchivedAssessments.CsvUpstream(options); break;
                case "compare-csvhelper-milestones": result = MilestoneComparison.Run(); break;
                case "qualify-application-workflow": result = await ApplicationQualification.Run(options.Get("--output", Path.Combine(Root, "evidence/generated/application-workflow"))); break;
                case "qualify-inventory-maturity": result = await InventoryQualification.Run(options.Get("--output", Path.Combine(Root, "evidence/generated/maturity/inventory"))); break;
                case "qualify-csharp-migration": result = await MigrationQualification.Run(options.Get("--output", Path.Combine(Root, "evidence/generated/csharp-migration/qualified")), options.Get("--engine-root")); break;
                default: throw new ArgumentException("Unknown tooling command: " + command);
            }
            Console.WriteLine(result.ToJsonString(Json.Options)); return result.S("executionStatus") is "failed" or "stopped_after_failure" ? 2 : 0;
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException or ArgumentException or InvalidOperationException or JsonException or System.Xml.XmlException or KeyNotFoundException)
        { Console.Error.WriteLine(Json.Create(("executionStatus", "failed"), ("failure", failure.Message)).ToJsonString()); return 2; }
    }
    private static async Task<int> RunTests(string command)
    {
        var group = command switch
        {
            "test-all" => "",
            "test-application-workflow" => "ApplicationBoundaryTests",
            "test-worker-protocol" => "WorkerBoundaryTests",
            "test-self-adjudication" => "AdjudicationBoundaryTests",
            "test-local-advice" => "GatewayBoundaryTests",
            "test-mutation-cases" => "MutationBoundaryTests",
            "test-shape-investigations" => "SampleEvidenceTests",
            "test-csvhelper-evidence" or "test-csvhelper-streaming" or "test-csvhelper-hefty" or "test-csvhelper-review" or "test-csvhelper-roundtrip" => "CsvBoundaryTests",
            _ => throw new ArgumentException("Unknown boundary-test group.")
        };
        var output = Path.Combine(Root, "evidence/generated/csharp-migration/boundaries", group.Length > 0 ? group : "all"); var manifest = Json.Create(("testDll", "tests/Assay.Tooling.Tests/bin/Release/net11.0/Assay.Tooling.Tests.dll"), ("minimumTests", group.Length == 0 ? 123 : group == "AdjudicationBoundaryTests" ? 6 : group == "ApplicationBoundaryTests" ? 22 : group == "WorkerBoundaryTests" ? 19 : group == "CsvBoundaryTests" ? 36 : group == "SampleEvidenceTests" ? 10 : 8), ("testReportProtocol", "xunit-mtp-v2"));
        var result = await ApplicationRunner.Tests(Root, manifest, output, selectedClass: group.Length > 0 ? "Assay.Tooling.Tests." + group : null); Json.Write(Path.Combine(output, "summary.json"), result); Console.WriteLine(result.ToJsonString(Json.Options)); return result.S("state") == "Passed" ? 0 : 2;
    }
    private static async Task<JsonObject> SelfAudit()
    {
        var tracked = (await Processes.Git(Root, "ls-files")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Json.Check(!tracked.Any(p => p.EndsWith(".py", StringComparison.Ordinal) && File.Exists(Path.Combine(Root, p))), "Tracked interpreter source remains.");
        foreach (var path in tracked.Where(p => (p.StartsWith("eng/", StringComparison.Ordinal) && Path.GetExtension(p) is ".sh" or ".ps1") || p == "eng/daa"))
        {
            Json.Check(!MyRegex.IsMatch(File.ReadAllText(Path.Combine(Root, path))), "Interpreter invocation remains: " + path);
        }

        var result = Json.Create(("schema", "assay-csharp-tooling-audit/0.1"), ("executionStatus", "completed"), ("interpreterFiles", 0), ("interpreterInvocations", 0), ("toolSha256", Json.Sha(Tool)), ("sdk", Sdk)); Json.Write(Path.Combine(Root, "evidence/generated/csharp-migration/self-audit.json"), result); return result;
    }

    [GeneratedRegex("(?i)python[0-9]?|\\.py(?:\\s|$)", RegexOptions.None, "")]
    private static partial Regex MyRegex { get; }
}
public sealed class Arguments
{
    private readonly Dictionary<string, List<string>> values = new(StringComparer.Ordinal);
    public List<string> Positionals { get; } = [];
    public Arguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--") { Positionals.AddRange(args[(i + 1)..]); break; }
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) { Positionals.Add(args[i]); continue; }
            var pair = args[i].Split('=', 2); var key = pair[0]; var value = pair.Length == 2 ? pair[1] : i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            if (!values.TryGetValue(key, out var entries))
            {
                values[key] = entries = [];
            }

            entries.Add(value);
        }
    }
    public bool Has(string key)
    {
        return values.TryGetValue(key, out var entries) && entries[^1] == "true";
    }

    public string? Get(string key)
    {
        return values.TryGetValue(key, out var entries) ? entries[^1] : null;
    }

    public string Get(string key, string fallback)
    {
        return Get(key) ?? fallback;
    }

    public string Required(string key)
    {
        return Get(key) ?? throw new ArgumentException("Missing argument: " + key);
    }

    public string[] All(string key)
    {
        return values.TryGetValue(key, out var entries) ? [.. entries] : [];
    }

    public int Count(string key, int fallback)
    {
        if (Get(key) is not { } text) { return fallback; }
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new ArgumentException("Expected integer argument: " + key);
    }
}
