using System.Security.Cryptography;
using System.Text.Json;
using ExpenseGuard.Domain;

namespace ExpenseGuard.Infrastructure;

public sealed record StructuralEvidence(string Verdict, bool Authoritative, string Scope, string Detail, string ReportSha256);
public static class BuildEvidence
{
    public static StructuralEvidence Read(string? directory)
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
            using var report = JsonDocument.Parse(reportBytes);
            var root = report.RootElement;
            var evidence = root.GetProperty("evidence");
            if (root.GetProperty("verdict").GetString() != "pass" || root.GetProperty("exitCode").GetInt32() != 0 ||
                !evidence.GetProperty("authoritative").GetBoolean() || evidence.GetProperty("verificationMode").GetString() != "verification")
                return Missing("The bound report does not contain an authoritative passing verification.");
            return new("Pass", true, "ExpenseGuard.Domain — selected structural rules; required business tests also executed",
                "Local hash-bound build evidence, not a signature or universal correctness/security proof.", reportHash);
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        { return Missing("Build evidence cannot be validated."); }
    }
    private static StructuralEvidence Missing(string detail) => new("NotAvailable", false, "ExpenseGuard.Domain", detail, "");
}
