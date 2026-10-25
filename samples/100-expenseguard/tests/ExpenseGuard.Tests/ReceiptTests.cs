using System.Security.Cryptography;
using System.Text;
using ExpenseGuard.Infrastructure;

namespace ExpenseGuard.Tests;

public sealed class ReceiptTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "eg-blobs-" + Guid.NewGuid().ToString("N"));
    private ReceiptBlobStore Store => new(directory);
    public static TheoryData<byte[], string> Formats => new()
    {
        {Encoding.ASCII.GetBytes("%PDF-1.7\nfixture"), "application/pdf"},
        {new byte[] {0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a,0}, "image/png"},
        {new byte[] {0xff,0xd8,0xff,0}, "image/jpeg"}
    };
    [Theory] [MemberData(nameof(Formats))]
    public async Task Uploaded_bytes_are_hashed_and_roundtrip(byte[] bytes, string contentType)
    {
        var id = Guid.NewGuid(); using var input = new MemoryStream(bytes);
        var stored = await Store.SaveAsync(id, input, TestContext.Current.CancellationToken);
        Assert.Equal(contentType, stored.ContentType); Assert.Equal(bytes.Length, stored.Bytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), stored.Sha256);
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Directory.GetFiles(directory).Single()));
        var read = await Store.ReadAsync(id, stored.Reference, stored.Sha256, TestContext.Current.CancellationToken);
        Assert.NotNull(read); Assert.Equal(bytes, read.Bytes);
        Assert.Null(await Store.ReadAsync(Guid.NewGuid(), stored.Reference, stored.Sha256, TestContext.Current.CancellationToken));
    }
    [Fact] public async Task Tampered_bytes_are_rejected()
    {
        var id = Guid.NewGuid(); using var input = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\noriginal"));
        var stored = await Store.SaveAsync(id, input, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Directory.GetFiles(directory).Single(), "%PDF-1.7\ntampered", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ReadAsync(id, stored.Reference, stored.Sha256, TestContext.Current.CancellationToken));
    }
    [Fact] public async Task Oversized_and_unsupported_receipts_leave_no_files()
    {
        using var oversized = new MemoryStream(new byte[ReceiptBlobStore.MaximumBytes + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.SaveAsync(Guid.NewGuid(), oversized, TestContext.Current.CancellationToken));
        using var unsupported = new MemoryStream(Encoding.ASCII.GetBytes("<script>not a receipt</script>"));
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.SaveAsync(Guid.NewGuid(), unsupported, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(directory));
    }
    [Theory] [InlineData("../../etc/passwd")] [InlineData("blob:../../private")] [InlineData("https://example.com/receipt")]
    public async Task Arbitrary_paths_and_remote_urls_are_never_read(string reference) =>
        Assert.Null(await Store.ReadAsync(Guid.NewGuid(), reference, new string('a', 64), TestContext.Current.CancellationToken));
    [Fact] public async Task Cancelled_upload_leaves_no_staged_file()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var input = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nfixture"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.SaveAsync(Guid.NewGuid(), input, cancelled.Token));
        Assert.False(Directory.Exists(directory));
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
