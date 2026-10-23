namespace Assay.Tooling;

public static class ApplicationQualification
{
    public static JsonObject Ready(JsonNode run, string folder)
    {
        Json.Check(run.S("executionStatus") == "completed" && run.S("workflowStatus") == "ReadyForReview", "Application did not reach complete review readiness.");
        var behavior = Json.Read(Path.Combine(folder, "behavior.json")); var mutations = Json.Read(Path.Combine(folder, "mutation.json"));
        return Json.Create(("tests", behavior.I("total")), ("workflowStatus", run.S("workflowStatus")), ("mutations", mutations.A("results").GroupBy(r => r.S("state"), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal)), ("decision", run["decision"]));
    }

    private static readonly string[] values = new[] { "Empty/whitespace content produces FetchFailed(Permanent).", "Nonempty content is preserved.", "Previous named tests and public contract remain compatible." };
    private static readonly string[] valuesArray = new[] { "nonblank-behavior" };

    public static async Task<JsonObject> Run(string output)
    {
        output = Path.GetFullPath(output); var path = Path.Combine(output, "qualification.json"); Json.Check(!File.Exists(path), "Choose a fresh output directory; prior qualification is retained.");
        var report = Json.Create(("schema", "assay-application-qualification/0.1"), ("executionStatus", "running"), ("providerRequests", 0), ("qualifierSha256", Json.Sha(Tool))); Json.Write(path, report);
        try
        {
            Console.WriteLine("Qualifying real Polly consumer and registered mutations.");
            var pollyFolder = Path.Combine(output, "polly"); var polly = await ApplicationWorkflow.Verify(Path.Combine(Root, "samples/70-polly-resilience/assay.app.json"), pollyFolder); report["polly"] = Ready(polly, pollyFolder);
            var app = Path.Combine(output, "undertaking-" + Guid.NewGuid().ToString("N"), "ReadyApp"); _ = await Scaffolding.Create(app, "ReadyApp"); var manifestPath = Path.Combine(app, "assay.app.json"); var baselineFolder = Path.Combine(output, "new-app-baseline"); var original = await ApplicationWorkflow.Verify(manifestPath, baselineFolder); report["newApplication"] = Ready(original, baselineFolder); report["newApplication"]!["path"] = app;
            var requestPath = Path.Combine(output, "change-request.json"); var request = Json.Create(("schema", "assay-change-request/0.1"), ("description", "Treat successful transport replies with blank content as permanent failure."), ("acceptanceCriteria", values)); Json.Write(requestPath, request); _ = ApplicationWorkflow.Plan(manifestPath, requestPath, Path.Combine(output, "change/work-item.json"));
            var requirementsPath = Path.Combine(app, "requirements.json"); var requirements = Json.Read(requirementsPath); requirements.A("requirements").Add(Json.Create(("id", "nonblank-content"), ("description", request.S("description")), ("probeIds", valuesArray))); Json.Write(requirementsPath, requirements);
            File.WriteAllText(Path.Combine(app, "tests/ReadyApp.Tests/BlankContentTests.cs"), """
                using ReadyApp.Domain;
                using Xunit;
                namespace ReadyApp.Tests;
                public sealed class BlankContentTests
                {
                    [Theory]
                    [InlineData("")]
                    [InlineData("  ")]
                    public void BlankSuccessfulContentIsPermanentFailure(string content)
                    {
                        var outcome = FetchDecisions.Convert(new(ReplyKind.Success, content));
                        Assert.Equal(new FetchFailed(FetchFailureKind.Permanent), Assert.IsType<FetchFailed>(outcome.Value));
                    }
                }
                """);
            var manifest = Json.Read(manifestPath); manifest["minimumTests"] = 16; manifest.A("probes").Add(Json.Create(("id", "nonblank-behavior"), ("requirementId", "nonblank-content"), ("module", "Behavior"), ("kind", "tests"))); Json.Write(manifestPath, manifest);
            var policyPath = Path.Combine(app, ".csassay.json"); var policy = Json.Read(policyPath); policy["release"]!.A("tests")[0]!["minimumExpectedTests"] = 16; Json.Write(policyPath, policy);
            (await Processes.Run(["dotnet", "format", "whitespace", Path.Combine(app, "App.slnx"), "--no-restore"], app, Path.Combine(output, "change/format.log"))).Require("Requirement test formatting");
            (await Processes.Run(["dotnet", "build", Path.Combine(app, "App.slnx"), "--no-restore", "-c", "Release"], app, Path.Combine(output, "change/red-build.log"))).Require("Requirement test build");
            var red = await ApplicationRunner.Tests(app, manifest, Path.Combine(output, "change/red"), 16); Json.Check(red.S("state") == "Failed" && red.I("failed") == 2, "New requirement did not produce two expected red tests."); red["inputSha256"] = ApplicationRunner.Snapshot(app, manifest); red["snapshotSha256"] = Json.Digest(red["inputSha256"]!); Json.Write(Path.Combine(output, "change/red.json"), red);
            var source = Path.Combine(app, "src/ReadyApp.Domain/FetchDecisions.cs"); var text = File.ReadAllText(source); const string old = "        ReplyKind.Success => new Fetched(reply.Content),"; Json.Check(text.Split(old, StringSplitOptions.None).Length == 2, "Requirement implementation span changed."); File.WriteAllText(source, text.Replace(old, "        ReplyKind.Success when string.IsNullOrWhiteSpace(reply.Content) => new FetchFailed(FetchFailureKind.Permanent),\n" + old, StringComparison.Ordinal));
            (await Processes.Run(["dotnet", "format", "whitespace", Path.Combine(app, "App.slnx"), "--no-restore"], app, Path.Combine(output, "change/implementation-format.log"))).Require("Implementation formatting");
            var candidateFolder = Path.Combine(output, "new-app-candidate"); var candidate = await ApplicationWorkflow.Verify(manifestPath, candidateFolder, Path.Combine(baselineFolder, "run.json")); var change = Ready(candidate, candidateFolder); change["redTests"] = red.I("failed"); change["previousTestsRetained"] = true; change["contractPinRetained"] = original.S("pinnedContractSha256") == candidate.S("pinnedContractSha256"); Json.Check(change.B("contractPinRetained"), "Candidate changed contract pin."); report["change"] = change;
            var legacy = Path.Combine(output, "legacy-intake"); _ = Directory.CreateDirectory(legacy); File.Copy(Path.Combine(Root, "global.json"), Path.Combine(legacy, "global.json")); var project = Path.Combine(legacy, "Legacy.csproj"); File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>\n"); var hash = Json.Sha(project); var intake = await ApplicationRunner.Intake(legacy, Json.Create(("solution", "Legacy.csproj")), Path.Combine(legacy, "execution")); Json.Check(intake.S("support") == "Unsupported" && Json.Sha(project) == hash, "Unsupported intake must preserve source."); Json.Write(Path.Combine(legacy, "intake.json"), intake); report["legacyIntake"] = "Unsupported; source unchanged";
            report["executionStatus"] = "completed"; report["scope"] = "Executed bounded starter and requirement-change loop; no release or provider approval.";
            var hashes = new JsonObject(); foreach (var file in new[] { "polly/run.json", "new-app-baseline/run.json", "new-app-candidate/run.json", "change/red.json", "legacy-intake/intake.json" })
            {
                hashes[file] = Json.Sha(Path.Combine(output, file));
            }

            report["evidenceSha256"] = hashes; Json.Write(path, report); return report;
        }
        catch (Exception error) { report["executionStatus"] = "failed"; report["failure"] = error.Message; Json.Write(path, report); throw; }
    }
}
