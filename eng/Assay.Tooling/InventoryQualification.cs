namespace Assay.Tooling;

public static class InventoryQualification
{
    private const string Sample = "samples/80-inventory-reservation";
    private static readonly string[] values = new[] { "Inventory.Tests.LimitTests.OverLimitDomainRequestIsInvalid", "Inventory.Tests.LimitTests.HttpOverLimitRequestDoesNotWrite", "Inventory.Tests.ReservationTests.HistoricalReceiptRemainsReplayable" };

    private static string FileAt(string root, string relative)
    {
        return Path.Combine(root, Sample, relative);
    }

    private static void SetMinimum(string root, int total, JsonNode manifest)
    {
        manifest["minimumTests"] = total; Json.Write(FileAt(root, "assay.app.json"), manifest); var path = Path.Combine(root, ".csassay.inventory.json"); var policy = Json.Read(path); policy["release"]!.A("tests")[0]!["minimumExpectedTests"] = total; Json.Write(path, policy);
    }
    public static async Task<JsonObject> Run(string output)
    {
        output = Path.GetFullPath(output); var path = Path.Combine(output, "qualification.json"); Json.Check(!File.Exists(path), "Choose a fresh qualification output; old evidence is retained.");
        var report = Json.Create(("schema", "assay-inventory-qualification/0.1"), ("executionStatus", "running"), ("providerRequests", 0), ("qualifierSha256", Json.Sha(Tool)), ("authority", "Executed bounded application evidence; not production or independent provider qualification.")); Json.Write(path, report);
        try
        {
            var root = Path.Combine(output, "inventory-app-" + Guid.NewGuid().ToString("N")); CopyTree(Path.Combine(Root, Sample), Path.Combine(root, Sample));
            foreach (var name in new[] { "Inventory.slnx", ".csassay.inventory.json", "global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", ".editorconfig", "NuGet.config" })
            {
                File.Copy(Path.Combine(Root, name), Path.Combine(root, name));
            }

            _ = Directory.CreateDirectory(Path.Combine(root, "tests")); File.Copy(Path.Combine(Root, "tests/Directory.Build.props"), Path.Combine(root, "tests/Directory.Build.props"));
            var configPath = Path.Combine(root, "NuGet.config"); var config = XDocument.Load(configPath); foreach (var source in config.Descendants("packageSources").Elements("add"))
            {
                if ((string?)source.Attribute("key") == "assay-local")
                {
                    source.SetAttributeValue("value", Path.Combine(Root, ".packages"));
                }
            }

            config.Save(configPath);
            var request = Json.Read(FileAt(root, "change-request.json")); var domain = FileAt(root, "src/Inventory.Domain/Reservations.cs"); var testPath = FileAt(root, "tests/Inventory.Tests/LimitTests.cs"); var testSource = File.ReadAllText(testPath);
            File.WriteAllText(domain, File.ReadAllText(domain).Replace(request.S("insertion"), "", StringComparison.Ordinal)); File.Delete(testPath);
            var requirementsPath = FileAt(root, "requirements.json"); var requirements = Json.Read(requirementsPath); requirements["requirements"] = Json.Array(requirements.A("requirements").Where(r => r.S("id") != request.S("requirementId"))); Json.Write(requirementsPath, requirements);
            var manifestPath = FileAt(root, "assay.app.json"); var manifest = Json.Read(manifestPath); manifest["probes"] = Json.Array(manifest.A("probes").Where(p => p.S("id") != request.S("probeId"))); SetMinimum(root, 25, manifest);
            Console.WriteLine("Qualifying independent inventory baseline with real PostgreSQL and HTTP."); var baselineFolder = Path.Combine(output, "baseline"); var original = await ApplicationWorkflow.Verify(manifestPath, baselineFolder); report["baseline"] = ApplicationQualification.Ready(original, baselineFolder); report["baseline"]!["path"] = root;
            _ = ApplicationWorkflow.Plan(manifestPath, FileAt(root, "change-request.json"), Path.Combine(output, "change/work-item.json"));
            requirements.A("requirements").Add(Json.Create(("id", request.S("requirementId")), ("description", request.S("description")), ("probeIds", new[] { request.S("probeId") }), ("criteria", new[] { Json.Create(("id", "limit-acceptance"), ("text", request.S("description")), ("testNames", values)) }))); Json.Write(requirementsPath, requirements);
            manifest.A("probes").Add(Json.Create(("id", request.S("probeId")), ("requirementId", request.S("requirementId")), ("module", "Behavior"), ("kind", "tests"))); SetMinimum(root, 27, manifest); File.WriteAllText(testPath, testSource);
            (await Processes.Run(["dotnet", "restore", Path.Combine(root, "Inventory.slnx"), "--locked-mode"], root, Path.Combine(output, "red/restore.log"))).Require("Inventory locked restore");
            (await Processes.Run(["dotnet", "format", "whitespace", Path.Combine(root, "Inventory.slnx"), "--no-restore"], root, Path.Combine(output, "red/format.log"))).Require("Acceptance test formatting");
            (await Processes.Run(["dotnet", "build", Path.Combine(root, "Inventory.slnx"), "--no-restore", "-c", "Release", "-m:2", "-nodeReuse:false"], root, Path.Combine(output, "red/build.log"))).Require("Acceptance test build");
            var red = await ApplicationRunner.Tests(root, manifest, Path.Combine(output, "red/tests"), 27); Json.Check(red.S("state") == "Failed" && red.I("failed") == 2, "Preregistered change did not fail two acceptance tests."); red["inputSha256"] = ApplicationRunner.Snapshot(root, manifest); red["snapshotSha256"] = Json.Digest(red["inputSha256"]!); Json.Write(Path.Combine(output, "red/result.json"), red);
            var text = File.ReadAllText(domain); Json.Check(text.Split(request.S("before"), StringSplitOptions.None).Length == 2, "Requirement span is ambiguous."); File.WriteAllText(domain, text.Replace(request.S("before"), request.S("insertion") + request.S("before"), StringComparison.Ordinal));
            var candidateFolder = Path.Combine(output, "candidate"); var candidate = await ApplicationWorkflow.Verify(manifestPath, candidateFolder, Path.Combine(baselineFolder, "run.json")); var ready = ApplicationQualification.Ready(candidate, candidateFolder); Json.Check(original.S("pinnedContractSha256") == candidate.S("pinnedContractSha256"), "Contract pin changed."); ready["redTests"] = red.I("failed"); ready["previousTestsRetained"] = true; ready["contractPinRetained"] = true; report["candidate"] = ready;
            var broken = Path.Combine(output, "wire-regression-" + Guid.NewGuid().ToString("N")); CopyTree(root, broken); var api = FileAt(broken, "src/Inventory.Api/ApiHost.cs"); text = File.ReadAllText(api); const string span = "int Remaining, string Reason"; Json.Check(text.Split(span, StringSplitOptions.None).Length == 2, "Wire regression span changed."); File.WriteAllText(api, text.Replace(span, "[property: JsonPropertyName(\"available\")] int Remaining, string Reason", StringComparison.Ordinal));
            var brokenManifest = FileAt(broken, "assay.app.json"); var badFolder = Path.Combine(output, "wire-regression"); var bad = await ApplicationWorkflow.Verify(brokenManifest, badFolder, Path.Combine(candidateFolder, "run.json")); Json.Check(bad.S("workflowStatus") == "Failed" && bad["decision"].S("action") == "RunRegisteredProbe" && bad["decision"].S("probeId") == "inventory-contract" && Json.Read(Path.Combine(badFolder, "behavior.json")).S("state") == "Passed", "Wire regression must pass typed behavior and request the registered contract probe.");
            var runPath = Path.Combine(badFolder, "run.json"); var investigation = await ApplicationWorkflow.Investigate(brokenManifest, runPath, "inventory-contract", Path.Combine(output, "investigation")); Json.Check(investigation.S("state") == "Failed" && investigation.A("issues").Count > 0, "Probe did not reproduce contract differences.");
            async Task Refused(string probe, string folder)
            {
                try { _ = await ApplicationWorkflow.Investigate(brokenManifest, runPath, probe, folder); throw new InvalidOperationException("An ineligible investigation obtained execution."); }
                catch (InvalidDataException) { Json.Check(!File.Exists(Path.Combine(folder, "probe-result.json")), "Refused probe left a completion artifact."); }
            }
            await Refused("not-registered", Path.Combine(output, "invalid-probe")); var originalApi = File.ReadAllBytes(api); File.AppendAllText(api, "\n// deliberate stale-source check\n"); try { await Refused("inventory-contract", Path.Combine(output, "stale-probe")); } finally { File.WriteAllBytes(api, originalApi); }
            var budgetBeforeRefusals = Json.Read(Path.Combine(badFolder, "probe-budget.json"));
            var binary = Inside(broken, Json.Read(brokenManifest).S("workerDll")); var originalBinary = File.ReadAllBytes(binary);
            File.WriteAllBytes(binary, [.. originalBinary, 0]);
            try { await Refused("inventory-contract", Path.Combine(output, "replaced-binary-probe")); }
            finally { File.WriteAllBytes(binary, originalBinary); }
            var originalManifest = File.ReadAllBytes(brokenManifest); var alteredManifest = Json.Read(brokenManifest); alteredManifest["maximumMutations"] = 7; Json.Write(brokenManifest, alteredManifest);
            try { await Refused("inventory-contract", Path.Combine(output, "changed-manifest-probe")); }
            finally { File.WriteAllBytes(brokenManifest, originalManifest); }
            Json.Check(JsonNode.DeepEquals(budgetBeforeRefusals, Json.Read(Path.Combine(badFolder, "probe-budget.json"))), "Refused freshness checks consumed investigation budget.");
            foreach (var number in new[] { 2, 3 })
            {
                _ = await ApplicationWorkflow.Investigate(brokenManifest, runPath, "inventory-contract", Path.Combine(output, "investigation-" + number));
            }

            await Refused("inventory-contract", Path.Combine(output, "exhausted"));
            report["wireRegression"] = Json.Create(("root", broken), ("behaviorTestsPassed", 27), ("workflowStatus", bad.S("workflowStatus")), ("decision", bad["decision"]), ("reproducedDifferences", investigation["issues"]), ("unregisteredRefused", true), ("staleRefused", true), ("replacedBinaryRefused", true), ("changedManifestRefused", true), ("refusalsPreserveBudget", true), ("budgetRefused", true));
            Json.Check(Json.Digest(ApplicationRunner.Snapshot(root, manifest)) == candidate.S("snapshotSha256"), "Qualified candidate changed during regression exercise.");
            // Qualification checks the reusable sample; it does not silently rewrite tracked source.
            foreach (var name in new[] { "src/Inventory.Domain/Reservations.cs", "tests/Inventory.Tests/LimitTests.cs" })
            {
                Json.Check(File.ReadAllText(FileAt(root, name)).TrimEnd() == File.ReadAllText(FileAt(Root, name)).TrimEnd(), "Qualified requirement implementation differs from the source sample.");
            }

            var hashes = new JsonObject(); foreach (var file in new[] { "baseline/run.json", "red/result.json", "candidate/run.json", "wire-regression/run.json", "investigation/probe-result.json" })
            {
                hashes[file] = Json.Sha(Path.Combine(output, file));
            }

            report["evidenceSha256"] = hashes; report["candidateSnapshotSha256"] = candidate.S("snapshotSha256"); report["executionStatus"] = "completed"; Json.Write(path, report); return report;
        }
        catch (Exception error) { report["executionStatus"] = "failed"; report["failure"] = error.Message; Json.Write(path, report); throw; }
    }
}
