namespace Assay.Tooling;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static JsonNode Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        Unique(document.RootElement);
        return JsonNode.Parse(text) ?? throw new InvalidDataException("Missing JSON value.");
    }
    private static void Unique(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("Duplicate JSON property: " + property.Name);
                }

                Unique(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                Unique(item);
            }
        }
    }
    public static JsonNode Read(string path)
    {
        return new FileInfo(path).Length > 4 * 1024 * 1024
            ? throw new InvalidDataException("Configuration exceeds the 4 MiB limit.")
            : Parse(File.ReadAllText(path));
    }
    public static JsonObject Create(params (string Key, object? Value)[] values)
    {
        var result = new JsonObject();
        foreach (var (key, value) in values)
        {
            result[key] = Node(value);
        }

        return result;
    }
    public static JsonNode? Node(object? value)
    {
        return value is JsonNode node ? node.DeepClone() : JsonSerializer.SerializeToNode(value, Options);
    }

    public static JsonArray Array<T>(IEnumerable<T> values)
    {
        return new(values.Select(v => Node(v)).ToArray());
    }

    public static void Write(string path, JsonNode value)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var pending = path + ".partial";
        File.WriteAllText(pending, value.ToJsonString(Options) + "\n");
        File.Move(pending, path, overwrite: true);
    }
    public static string Sha(string path)
    {
        return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }

    public static string Hash(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public static string Digest(JsonNode node)
    {
        return Hash(Canonical(node));
    }

    private static string Canonical(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => "{" + string.Join(',', obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value))) + "}",
            JsonArray arr => "[" + string.Join(',', arr.Select(Canonical)) + "]",
            _ => node?.ToJsonString() ?? "null"
        };
    }

    public static string S(this JsonNode? value, string key, string fallback = "")
    {
        return value?[key]?.GetValue<string>() ?? fallback;
    }

    public static int I(this JsonNode? value, string key, int fallback = 0)
    {
        return value?[key] is { } node ? node.GetValueKind() == JsonValueKind.Number && int.TryParse(node.ToJsonString(), out var number) ? number : throw new InvalidDataException("Expected integer: " + key) : fallback;
    }

    public static long L(this JsonNode? value, string key, long fallback = 0)
    {
        return value?[key] is { } node ? node.GetValueKind() == JsonValueKind.Number && long.TryParse(node.ToJsonString(), out var number) ? number : throw new InvalidDataException("Expected integer: " + key) : fallback;
    }

    public static bool B(this JsonNode? value, string key, bool fallback = false)
    {
        return value?[key]?.GetValue<bool>() ?? fallback;
    }

    public static JsonArray A(this JsonNode? value, string key)
    {
        return value?[key]?.AsArray() ?? [];
    }

    public static string[] Strings(this JsonNode? value, string key)
    {
        return value.A(key).Select(n => n!.GetValue<string>()).ToArray();
    }

    public static void Check([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}

public static class EnvironmentPaths
{
    public static readonly string Root = FindRoot();
    public static string Engine => Path.GetFullPath(Environment.GetEnvironmentVariable("ASSAY_RUNNER") ?? Path.Combine(Root, "../CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll"));
    public static string Decision => Path.Combine(Root, "src/Assay.Workflow.Cli/bin/Release/net11.0/Assay.Workflow.Cli.dll");
    public static string Advice => Path.Combine(Root, "samples/50-decision-assistance/src/DecisionAssist.Cli/bin/Release/net11.0/DecisionAssist.Cli.dll");
    public static string Tool => Path.Combine(Root, "eng/Assay.Tooling/bin/Release/net11.0/Assay.Tooling.dll");
    public static string Sdk => Json.Read(Path.Combine(Root, "global.json"))["sdk"].S("version");
    public static readonly HashSet<string> Excluded = ["bin", "obj", ".git", ".packages", ".upstream-cache", ".candidate-cache", "TestResults", "artifacts"];
    public static string FindRoot()
    {
        foreach (var origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var folder = new DirectoryInfo(origin); folder is not null; folder = folder.Parent)
            {
                if (File.Exists(Path.Combine(folder.FullName, "AllSamples.slnx")))
                {
                    return folder.FullName;
                }
            }
        }

        throw new InvalidOperationException("Cannot locate the Assay toolkit checkout.");
    }
    public static string Relative(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    public static string Inside(string root, string value)
    {
        root = Path.GetFullPath(root);
        var result = Path.GetFullPath(Path.Combine(root, value));
        Json.Check(result == root || result.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Project path escapes its declared workspace: " + value);
        var current = root;
        foreach (var part in Path.GetRelativePath(root, result).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null)
            {
                var target = info.ResolveLinkTarget(true)?.FullName ?? throw new InvalidDataException("Unresolved source link.");
                Json.Check(target == root || target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Source link escapes its workspace.");
            }
        }
        return result;
    }
    public static IEnumerable<string> Files(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                var relative = Relative(root, path);
                if (relative.Split('/').Any(Excluded.Contains) || relative is "evidence/generated" || relative.StartsWith("evidence/generated/", StringComparison.Ordinal)) { continue; }
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                Json.Check(info.LinkTarget is null, "Declared source inventories cannot contain symbolic links: " + relative);
                if (info is DirectoryInfo) { pending.Push(path); }
                else { yield return path; }
            }
        }
    }

    public static void CopyTree(string source, string destination)
    {
        _ = Directory.CreateDirectory(destination);
        foreach (var file in Files(source))
        {
            var target = Inside(destination, Relative(source, file));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}

public enum ProcessState { Passed, Failed, TimedOut, Cancelled }
public sealed record Execution(ProcessState State, int ExitCode, string Output)
{
    public string StateName => State.ToString();
    public void Require(string label)
    {
        if (State != ProcessState.Passed)
        {
            throw new InvalidOperationException(label + ": " + State + "; inspect retained execution log.");
        }
    }
}
public static class Processes
{
    public static async Task<Execution> Run(IEnumerable<string> arguments, string cwd, string? log = null, double seconds = 180, IReadOnlyDictionary<string, string>? environment = null, CancellationToken token = default)
    {
        var args = arguments.ToArray();
        Json.Check(args.Length > 0, "Missing process identity.");
        using var process = new Process { StartInfo = new ProcessStartInfo(args[0]) { UseShellExecute = false, WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var arg in args.Skip(1))
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                process.StartInfo.Environment[key] = value;
            }
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(seconds));
        var interrupted = false;
        void OnCancel(object? sender, ConsoleCancelEventArgs e) { e.Cancel = true; interrupted = true; lifetime.Cancel(); }
        ConsoleCancelEventHandler onCancel = OnCancel;
        Console.CancelKeyPress += onCancel;
        try
        {
            _ = process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var state = ProcessState.Passed;
            try { await process.WaitForExitAsync(lifetime.Token); }
            catch (OperationCanceledException)
            {
                state = token.IsCancellationRequested || interrupted ? ProcessState.Cancelled : ProcessState.TimedOut;
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync(CancellationToken.None);
            }
            if (state == ProcessState.Passed && process.ExitCode != 0)
            {
                state = ProcessState.Failed;
            }

            var output = await stdout + await stderr;
            if (log is not null) { _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(log))!); File.WriteAllText(log, output); }
            return new(state, process.ExitCode, output);
        }
        finally { Console.CancelKeyPress -= onCancel; }
    }
    public static async Task<string> Git(string root, params string[] args)
    {
        var result = await Run(["git", "-C", root, .. args], root);
        result.Require("Local Git inspection");
        return result.Output.Trim();
    }
}

public sealed class TemporaryWorkspace : IDisposable
{
    public string Path { get; }
    public TemporaryWorkspace(string prefix = "assay-", string? parent = null) => Path = parent is null ? Directory.CreateTempSubdirectory(prefix).FullName : Directory.CreateDirectory(System.IO.Path.Combine(parent, prefix + Guid.NewGuid().ToString("N"))).FullName;
    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
