using System.Security.Cryptography;

namespace ExpenseGuard.Infrastructure;

public sealed record StoredReceipt(string Reference, string Sha256, string ContentType, int Bytes);
public sealed record ReceiptContent(byte[] Bytes, string ContentType);

// Private local storage. Opaque references never become arbitrary filesystem paths.
// Type signatures are an allowlist, not malware or authenticity verification.
public sealed class ReceiptBlobStore(string directory)
{
    public const int MaximumBytes = 3 * 1024 * 1024;
    public async Task<StoredReceipt> SaveAsync(Guid claimId, Stream input, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var block = new byte[8192];
        while (true)
        {
            var count = await input.ReadAsync(block, ct);
            if (count == 0) break;
            if (buffer.Length + count > MaximumBytes) throw new InvalidDataException("Receipt exceeds 3 MiB.");
            await buffer.WriteAsync(block.AsMemory(0, count), ct);
        }
        var bytes = buffer.ToArray(); var type = ContentType(bytes);
        var id = Guid.NewGuid().ToString("N");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var reference = "blob:" + claimId.ToString("N") + ":" + id;
        var final = Resolve(reference) ?? throw new InvalidDataException("Invalid receipt identity.");
        var pending = final + ".pending";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var file = new FileStream(pending, options)) await file.WriteAsync(bytes, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(pending, final);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
        return new(reference, Convert.ToHexStringLower(SHA256.HashData(bytes)), type, bytes.Length);
    }
    public async Task<ReceiptContent?> ReadAsync(Guid claimId, string reference, string expectedSha256, CancellationToken ct)
    {
        var path = reference.StartsWith("blob:" + claimId.ToString("N") + ":", StringComparison.Ordinal) ? Resolve(reference) : null;
        if (path is null || !File.Exists(path)) return null;
        var size = new FileInfo(path).Length;
        if (size is <= 0 or > MaximumBytes) throw new InvalidDataException("Stored receipt size is invalid.");
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != expectedSha256) throw new InvalidDataException("Stored receipt hash differs from its claim evidence.");
        return new(bytes, ContentType(bytes));
    }
    public void Remove(string reference)
    {
        if (Resolve(reference) is string path && File.Exists(path)) File.Delete(path);
    }
    private string? Resolve(string reference) => reference.StartsWith("blob:", StringComparison.Ordinal) &&
        reference.Length == 70 && reference[37] == ':' && Guid.TryParseExact(reference[5..37], "N", out _) && Guid.TryParseExact(reference[38..], "N", out _)
            ? Path.Combine(directory, reference[5..37] + "_" + reference[38..] + ".blob") : null;
    private static string ContentType(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0x25, 0x50, 0x44, 0x46, 0x2d, ..] => "application/pdf",
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, ..] => "image/png",
        [0xff, 0xd8, 0xff, ..] => "image/jpeg",
        _ => throw new InvalidDataException("Only PDF, PNG or JPEG receipt signatures are accepted.")
    };
}
