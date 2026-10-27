using System.Security.Cryptography;
using System.Text.Json;
using ExpenseGuard.Infrastructure;

namespace ExpenseGuard.Api;

// A local CLI-only probe. No HTTP route can copy files, edit fixtures or execute code.
internal static class GuidedFreshness
{
    public static int Run(string[] args)
    {
        if (args.Length != 3) { Console.Error.WriteLine("Usage: --guided-freshness existing-qualification fresh-output"); return 64; }
        var directory = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) { Console.Error.WriteLine("Choose fresh output; previous evidence is retained."); return 2; }
        var before = BuildEvidence.Read(directory);
        if (!before.Authoritative) { Console.Error.WriteLine("Current bound verification required: " + before.Detail); return 2; }
        var sample = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(sample.FullName, "ExpenseGuard.slnx"))) sample = sample.Parent ?? throw new InvalidOperationException("Sample root unavailable.");
        Directory.CreateDirectory(output); var copy = Path.Combine(output, "copy"); Directory.CreateDirectory(copy);
        foreach (var path in BuildEvidence.ApplicationFiles.Concat(BuildEvidence.ConfigurationFiles))
        {
            var target = Path.Combine(copy, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Path.Combine(sample.FullName, path), target);
        }
        foreach (var name in new[] { "build-evidence.json", "verify.json" }) File.Copy(Path.Combine(directory, name), Path.Combine(copy, name));
        if (!BuildEvidence.Read(copy, copy).Authoritative) throw new InvalidDataException("Unedited copy lost its binding.");
        File.AppendAllText(Path.Combine(copy, "src/ExpenseGuard.Api/wwwroot/index.html"), "\n<!-- disposable freshness control -->\n");
        var stale = BuildEvidence.Read(copy, copy);
        if (stale.Authoritative || stale.Verdict != "NotAvailable") throw new InvalidDataException("Changed asset retained Pass.");
        var record = new
        {
            kind = "scenario", id = "D06", title = "Changed web asset invalidates application evidence", status = "Recorded example",
            expected = "Bound Pass before change; NotAvailable after declared asset change", input = "Disposable copy of all five application assemblies and declared configuration; original source unchanged",
            observation = new { detector = "ExpenseGuard BuildEvidence reader", before = before.Verdict, after = stale.Verdict, authoritativeAfter = stale.Authoritative,
                reportSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "verify.json")))) },
            limits = "Local trusted-storage hash binding, not remote attestation. NotAvailable is the viewer contract, not an engine verdict."
        };
        File.WriteAllText(Path.Combine(output, "observation.json"), JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("Bound Pass -> changed declared asset -> NotAvailable. Original application/receipt untouched."); return 0;
    }
}
