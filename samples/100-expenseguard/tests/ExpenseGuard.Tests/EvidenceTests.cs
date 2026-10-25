using System.Security.Cryptography;
using System.Text.Json;
using ExpenseGuard.Domain;
using ExpenseGuard.Infrastructure;

namespace ExpenseGuard.Tests;

public sealed class EvidenceTests
{
    [Fact] public void Absent_build_evidence_never_claims_pass() => Assert.Equal("NotAvailable", BuildEvidence.Read(null).Verdict);
    [Fact] public void Malformed_build_evidence_never_claims_pass()
    {
        var root = Path.Combine(Path.GetTempPath(), "eg-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { File.WriteAllText(Path.Combine(root, "build-evidence.json"), "{}"); Assert.False(BuildEvidence.Read(root).Authoritative); }
        finally { Directory.Delete(root, true); }
    }
    [Theory] [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public void Stale_binding_or_provisional_report_never_claims_pass(bool staleAssembly, bool provisional)
    {
        var root = Path.Combine(Path.GetTempPath(), "eg-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var report = JsonSerializer.SerializeToUtf8Bytes(new { verdict = "pass", exitCode = 0,
                evidence = new { authoritative = !provisional, verificationMode = provisional ? "analysis" : "verification" } });
            File.WriteAllBytes(Path.Combine(root, "verify.json"), report);
            var hash = Convert.ToHexStringLower(SHA256.HashData(report));
            var assembly = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(ExpenseClaim).Assembly.Location)));
            File.WriteAllText(Path.Combine(root, "build-evidence.json"), JsonSerializer.Serialize(new
            { domainAssemblySha256 = staleAssembly ? "stale" : assembly, reportSha256 = staleAssembly || provisional ? hash : "stale" }));
            Assert.False(BuildEvidence.Read(root).Authoritative);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact] public void Hash_chain_detects_snapshot_modification_and_sequence_change()
    {
        var at = DateTimeOffset.UnixEpoch;
        var hash = ReceiptHashes.Compute("GENESIS", 1, "Submitted", "alice", "Employee", at, "{}");
        var receipt = new ExpenseGuard.Application.AuditReceipt(1, "Submitted", "alice", "Employee", at, "{}", "GENESIS", hash);
        Assert.True(ReceiptHashes.Verify([receipt]));
        Assert.False(ReceiptHashes.Verify([receipt with { SnapshotJson = "{\"changed\":true}" }]));
        Assert.False(ReceiptHashes.Verify([receipt with { Sequence = 2 }]));
        Assert.False(ReceiptHashes.Verify([]));
    }
}
