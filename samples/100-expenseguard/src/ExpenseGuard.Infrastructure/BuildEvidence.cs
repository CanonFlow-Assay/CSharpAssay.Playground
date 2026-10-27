using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Immutable;
using System.Reflection;
using ExpenseGuard.Application;
using ExpenseGuard.Domain;

namespace ExpenseGuard.Infrastructure;

public sealed record StructuralEvidence(string Verdict, bool Authoritative, string Scope, string Detail, string ReportSha256);
public static class BuildEvidence
{
    public static ImmutableArray<string> ApplicationFiles { get; } = [.. new[] { "Domain", "Application", "Infrastructure", "Api", "Worker" }
        .Select(p => $"src/ExpenseGuard.{p}/bin/Release/net11.0/ExpenseGuard.{p}.dll")];
    public static ImmutableArray<string> ConfigurationFiles { get; } = [".csassay.json", "global.json", "Directory.Build.props", "Directory.Packages.props",
        .. new[] { "Domain", "Application", "Infrastructure", "Api", "Worker" }.Select(p => $"src/ExpenseGuard.{p}/packages.lock.json"),
        "tests/ExpenseGuard.Tests/packages.lock.json", "src/ExpenseGuard.Api/wwwroot/app.js", "src/ExpenseGuard.Api/wwwroot/app.css", "src/ExpenseGuard.Api/wwwroot/index.html",
        "src/ExpenseGuard.Api/wwwroot/guide.js", "src/ExpenseGuard.Api/wwwroot/recorded.js", "src/ExpenseGuard.Api/wwwroot/guide-examples.json"];
    public static Dictionary<string, string> HashFiles(string root, ImmutableArray<string> files) =>
        files.ToDictionary(p => p, p => Hash(Path.Combine(root, p)), StringComparer.Ordinal);

    public static StructuralEvidence Read(string? directory, string? checkoutRoot = null)
    {
        if (string.IsNullOrWhiteSpace(directory)) return Missing("Build evidence is not configured.");
        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "build-evidence.json")));
            var m = manifest.RootElement;
            var reportBytes = File.ReadAllBytes(Path.Combine(directory, "verify.json"));
            var reportHash = Convert.ToHexStringLower(SHA256.HashData(reportBytes));
            var assembly = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(ExpenseClaim).Assembly.Location)));
            if (m.GetProperty("domainAssemblySha256").GetString() != assembly || m.GetProperty("reportSha256").GetString() != reportHash)
                return Missing("Build assembly or report differs from the qualification binding.");
            checkoutRoot ??= FindSample();
            if (checkoutRoot is null || m.GetProperty("schema").GetString() != "expenseguard-build-binding/0.2")
                return Missing("Application assemblies/configuration are unbound; application-test freshness is unavailable.");
            foreach (var (field, paths) in new[] { ("applicationAssemblySha256", ApplicationFiles), ("configurationSha256", ConfigurationFiles) })
            {
                var hashes = m.GetProperty(field);
                if (hashes.EnumerateObject().Count() != paths.Length) return Missing("Application binding is incomplete.");
                foreach (var path in paths)
                    if (hashes.GetProperty(path).GetString() != Hash(Path.Combine(checkoutRoot, path)))
                        return Missing("Stale application evidence: an executable assembly or declared verification input changed.");
            }
            // Compare loaded components too: pointing the viewer at an old build
            // directory must not disguise a newer running API/shell assembly.
            var loaded = new[] { typeof(ExpenseClaim).Assembly, typeof(ExpenseService).Assembly, typeof(BuildEvidence).Assembly, Assembly.GetEntryAssembly() }
                .Where(a => a is not null && ApplicationFiles.Any(p => p.EndsWith("/" + a.GetName().Name + ".dll", StringComparison.Ordinal)));
            foreach (var item in loaded)
            {
                var path = ApplicationFiles.Single(p => p.EndsWith("/" + item!.GetName().Name + ".dll", StringComparison.Ordinal));
                if (m.GetProperty("applicationAssemblySha256").GetProperty(path).GetString() != Hash(item!.Location))
                    return Missing("Stale application evidence: running assembly differs from the tested build.");
            }
            using var report = JsonDocument.Parse(reportBytes);
            var root = report.RootElement;
            var evidence = root.GetProperty("evidence");
            if (root.GetProperty("verdict").GetString() != "pass" || root.GetProperty("exitCode").GetInt32() != 0 ||
                !evidence.GetProperty("authoritative").GetBoolean() || evidence.GetProperty("verificationMode").GetString() != "verification")
                return Missing("The bound report does not contain an authoritative passing verification.");
            return new("Pass", true, "ExpenseGuard.Domain selected structural rules; required application tests bound to all five executable assemblies and declared sample configuration",
                "Local hash binding, not a signature. Deployment environment, identity/FX/provider secrets and operations are not qualified. Worker freshness covers the configured build file, not an independently deployed worker process.", reportHash);
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        { return Missing("Build evidence cannot be validated."); }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string? FindSample()
    {
        for (var folder = new DirectoryInfo(Path.GetDirectoryName(typeof(BuildEvidence).Assembly.Location)!); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "ExpenseGuard.slnx"))) return folder.FullName;
        return null;
    }
    private static StructuralEvidence Missing(string detail) => new("NotAvailable", false, "ExpenseGuard.Domain", detail, "");
}
