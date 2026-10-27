using System.IO.Compression;
namespace Assay.Tooling;

public static class PackageTools
{
    public static string PackedTool
    {
        get
        {
            var binding = Json.Read(Path.Combine(Root, ".packages/tool-binding.json")); var path = Inside(Root, binding.S("path"));
            Json.Check(binding.S("packageSha256") == Json.Sha(Path.Combine(Root, ".packages/CsAssay.Tool.0.2.0-rc.1.nupkg")) && binding.S("cliSha256") == Json.Sha(path), "Installed tool differs from its exact local package binding.");
            ValidateInstalledPayload(Path.Combine(Root, ".packages/CsAssay.Tool.0.2.0-rc.1.nupkg"), Path.GetDirectoryName(path)!);
            return path;
        }
    }

    public static async Task<JsonObject> InstallPackedTool()
    {
        var package = Path.Combine(Root, ".packages/CsAssay.Tool.0.2.0-rc.1.nupkg"); var digest = Json.Sha(package);
        var destination = Path.Combine(Root, ".packages/installed-assay", digest);
        if (!Directory.Exists(destination))
            (await Processes.Run(["dotnet", "tool", "install", "CsAssay.Tool", "--version", "0.2.0-rc.1", "--tool-path", destination, "--configfile", Path.Combine(Root, "NuGet.config"), "--no-cache"], Root, Path.Combine(Root, ".packages/tool-install-" + digest + ".log"))).Require("Fresh package-scoped tool installation");
        var cli = Directory.GetFiles(destination, "cs-assay.dll", SearchOption.AllDirectories).Single();
        var directory = Path.GetDirectoryName(cli)!;
        ValidateInstalledPayload(package, directory);
        var binding = Json.Create(("schema", "assay-installed-tool/0.1"), ("packageSha256", digest), ("path", Relative(Root, cli)), ("cliSha256", Json.Sha(cli)),
            ("scope", "Exact package-scoped install; same-version global resolver state is not authority."));
        Json.Write(Path.Combine(Root, ".packages/tool-binding.json"), binding); return binding;
    }

    public static void ValidateInstalledPayload(string package, string directory)
    {
        using var zip = ZipFile.OpenRead(package);
        var entries = zip.Entries.Where(e => e.FullName.StartsWith("tools/net11.0/any/", StringComparison.Ordinal) && e.Name.Length > 0).ToArray();
        Json.Check(entries.Any(e => e.FullName == "tools/net11.0/any/cs-assay.dll"), "Tool package lacks its runtime entry point.");
        foreach (var entry in entries)
        {
            var installed = Inside(directory, entry.FullName["tools/net11.0/any/".Length..]);
            using var input = entry.Open(); var expected = Convert.ToHexStringLower(SHA256.HashData(input));
            Json.Check(File.Exists(installed) && Json.Sha(installed) == expected, "Installed payload differs from package: " + entry.FullName);
        }
    }
    private static readonly string[] Inputs = ["src/Assay.Workflow.Domain", "src/Assay.Workflow.Cli", "samples/50-decision-assistance/src/DecisionAssist.Domain", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.config", ".editorconfig", "eng/prepare-application-workflow.sh", "eng/Assay.Tooling"];
    private static readonly string[] sourceArray = new[] { "samples", "src", "tests", "eng/Assay.Tooling" };

    public static async Task<string> SdkSource()
    {
        var dirty = await Processes.Git(Root, ["status", "--porcelain", "--untracked-files=all", "--", .. Inputs]);
        Json.Check(dirty.Length == 0, "Commit exact SDK producer inputs before preparing packages.");
        var revision = await Processes.Git(Root, ["log", "-1", "--format=%H", "--", .. Inputs]);
        Json.Check(revision.Length > 0, "No committed SDK producer inputs found.");
        _ = await Processes.Git(Root, ["diff", "--exit-code", revision, "--", .. Inputs]);
        Json.Write(Path.Combine(Root, ".packages/sdk-source.json"), Json.Create(("schema", "assay-sdk-source/0.1"), ("sourceCommit", revision), ("producerInputs", Inputs), ("scope", "Exact committed SDK producer inputs; consumer-only commits do not alter package identity.")));
        File.WriteAllText(Path.Combine(Root, ".packages/sdk-source.props"), $"<Project><PropertyGroup><SourceRevisionId>{revision}</SourceRevisionId><AssaySdkProducerRevision>{revision}</AssaySdkProducerRevision></PropertyGroup></Project>\n");
        return revision;
    }
    public static void Normalize(string path)
    {
        Json.Check(Path.GetFileName(path) is "CanonFlow.Assay.Workflow.0.1.0-preview.1.nupkg" or "CanonFlow.DecisionAssist.0.1.0-preview.1.nupkg", "Only the two local preview SDK packages may be normalized.");
        var contents = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using (var archive = ZipFile.OpenRead(path))
        {
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                Json.Check(!contents.ContainsKey(name) && name != ".signature.p7s" && !name.StartsWith('/') && !name.Contains('\\') && !name.Split('/').Contains("..", StringComparer.Ordinal), "Signed, duplicate or unsafe entries cannot be normalized.");
                using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory); contents[name] = memory.ToArray();
            }
        }

        var pending = path + ".partial";
        try
        {
            File.Delete(pending);
            using (var archive = ZipFile.Open(pending, ZipArchiveMode.Create))
            {
                foreach (var (name, bytes) in contents)
                {
                    var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize); entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero); entry.ExternalAttributes = unchecked((int)(0x81a4U << 16));
                    using var stream = entry.Open(); stream.Write(bytes);
                }
            }

            using (var archive = ZipFile.OpenRead(pending))
            {
                foreach (var entry in archive.Entries)
                {
                    using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory);
                    Json.Check(contents[entry.FullName].SequenceEqual(memory.ToArray()), "Package normalization changed entry contents.");
                }
            }

            File.Move(pending, path, overwrite: true);
        }
        finally { File.Delete(pending); }
    }
    public static async Task CopyQualified(string engineRoot, string destination)
    {
        engineRoot = Path.GetFullPath(engineRoot);
        var qualification = Path.Combine(engineRoot, "artifacts/csharp-tooling-qualified");
        var report = Json.Read(Path.Combine(qualification, "qualification.json"));
        var head = await Processes.Git(engineRoot, "rev-parse", "HEAD");
        Json.Check((await Processes.Git(engineRoot, "status", "--porcelain")).Length == 0 && report.S("sourceCommit") == head && report.S("executionStatus") == "completed" && report.B("releaseQualified") && report.B("sourceUnchanged"), "Qualified preparation requires the exact clean qualified engine commit.");
        Json.Check(report.A("gates").Count == 7, "Engine qualification gate set is incomplete.");
        foreach (var gate in report.A("gates"))
        {
            Json.Check(gate.S("state") == "Passed" && Json.Sha(Inside(qualification, gate.S("log"))) == gate.S("logSha256"), "Engine gate or artifact integrity failed.");
        }

        var packages = Path.Combine(engineRoot, "artifacts/packages");
        var checksums = File.ReadAllLines(Path.Combine(packages, "checksums.sha256")).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToDictionary(a => a[1], a => a[0], StringComparer.Ordinal);
        _ = Directory.CreateDirectory(destination);
        foreach (var name in new[] { "CsAssay.Analyzers.0.2.0-rc.1.nupkg", "CsAssay.Tool.0.2.0-rc.1.nupkg" })
        {
            Json.Check(checksums.TryGetValue(name, out var hash) && Json.Sha(Path.Combine(packages, name)) == hash, "Qualified package checksum differs: " + name);
            File.Copy(Path.Combine(packages, name), Path.Combine(destination, name), overwrite: true);
        }
    }
    public static async Task<JsonObject> AuditToolchain(string output)
    {
        var report = Json.Create(("schema", "assay-all-samples-toolchain/0.1"), ("executionStatus", "running"), ("projects", new JsonArray()), ("rejectionChecks", new JsonArray()));
        Json.Write(output, report);
        try
        {
            var spec = Json.Read(Path.Combine(Root, "workflows/sample-toolchain.json")); var pin = Json.Read(Path.Combine(Root, "global.json"))["sdk"]!;
            var sdk = await Processes.Run(["dotnet", "--version"], Root); sdk.Require("SDK pin inspection");
            Json.Check(sdk.Output.Trim() == pin.S("version") && pin.S("rollForward") == "disable", "Exact SDK pin is not satisfied."); report["sdk"] = sdk.Output.Trim();
            var projects = sourceArray.SelectMany(f => Files(Path.Combine(Root, f))).Where(p => Path.GetExtension(p) == ".csproj").Select(p => Relative(Root, p)).Order(StringComparer.Ordinal).ToArray();
            var declared = XDocument.Load(Path.Combine(Root, "AllSamples.slnx")).Descendants("Project").Select(p => (string)p.Attribute("Path")!).Order(StringComparer.Ordinal).ToArray();
            Json.Check(projects.SequenceEqual(declared), "AllSamples.slnx must include every authored project exactly once.");
            var exceptions = spec.A("negativeControls").ToDictionary(p => p.S("project"), p => p.S("reason"), StringComparer.Ordinal);
            Json.Check(exceptions.Keys.All(projects.Contains), "Negative-control ledger includes a missing project.");
            foreach (var project in projects)
            {
                var execution = await Processes.Run(["dotnet", "msbuild", project, "-nologo", "-getProperty:TargetFramework,TargetFrameworks,LangVersion,Nullable,TreatWarningsAsErrors,NETCoreSdkVersion,NoWarn"], Root); execution.Require("Evaluate " + project);
                var props = Json.Parse(execution.Output)["Properties"]!;
                var frameworks = (props.S("TargetFrameworks") is { Length: > 0 } tfms ? tfms : props.S("TargetFramework")).Split(';');
                Json.Check(frameworks.Length > 0 && frameworks.All(t => t == "net11.0" || t.StartsWith("net11.0-", StringComparison.Ordinal)) && props.S("LangVersion") == "preview" && props.S("NETCoreSdkVersion") == pin.S("version"), "Native toolchain mandate violated: " + project);
                Json.Check(exceptions.ContainsKey(project) || (props.S("Nullable") == "enable" && props.S("TreatWarningsAsErrors").Equals("true", StringComparison.OrdinalIgnoreCase) && !props.S("NoWarn").Contains("CSA", StringComparison.OrdinalIgnoreCase)), "Compiler mandate violated: " + project);
                report.A("projects").Add(Json.Create(("path", project), ("properties", props), ("classification", exceptions.ContainsKey(project) ? "reviewed-negative-control" : "strict-positive"), ("reason", exceptions.GetValueOrDefault(project, ""))));
            }
            (string Setting, string Code)[] cases = [("TargetFramework=net10.0", "CSASSAY-NET11-REQUIRED"), ("TargetFrameworks=net11.0%3Bnet10.0", "CSASSAY-NET11-REQUIRED"), ("LangVersion=latest", "CSASSAY-NATIVE-LANGUAGE-REQUIRED"), ("Nullable=disable", "CSASSAY-NULLABLE-REQUIRED"), ("TreatWarningsAsErrors=false", "CSASSAY-WARNINGS-AS-ERRORS-REQUIRED"), ("NETCoreSdkVersion=0.0.0", "CSASSAY-SDK-PIN-REQUIRED")];
            foreach (var (setting, code) in cases)
            {
                var log = Path.Combine(Path.GetDirectoryName(output)!, "rejection-" + report.A("rejectionChecks").Count + ".log");
                var execution = await Processes.Run(["dotnet", "msbuild", "src/Assay.Workflow.Domain/Assay.Workflow.Domain.csproj", "-nologo", "-t:PrepareForBuild", "-p:" + setting], Root, log);
                Json.Check(execution.ExitCode != 0 && execution.Output.Contains(code, StringComparison.Ordinal), "Strict override not rejected with " + code);
                report.A("rejectionChecks").Add(Json.Create(("override", setting), ("expectedCode", code), ("exitCode", execution.ExitCode), ("log", log)));
            }
            report["executionStatus"] = "completed"; report["sampleProjects"] = projects.Count(p => p.StartsWith("samples/", StringComparison.Ordinal)); report["totalProjects"] = projects.Length; report["historicalEntries"] = spec["historicalEntries"]!.DeepClone();
            Json.Write(output, report); return report;
        }
        catch (Exception error) { report["executionStatus"] = "failed"; report["failure"] = error.Message; Json.Write(output, report); throw; }
    }
}
