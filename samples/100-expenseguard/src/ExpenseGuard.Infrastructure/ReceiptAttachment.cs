using ExpenseGuard.Application;
using ExpenseGuard.Domain;

namespace ExpenseGuard.Infrastructure;

public static class ReceiptAttachment
{
    public static async Task<StoreResult> UploadAsync(ReceiptBlobStore blobs, ExpenseService service, ExpenseId id,
        int version, bool itemized, Actor actor, Stream input, CancellationToken ct)
    {
        var saved = await blobs.SaveAsync(id.Value, input, ct);
        // Filesystem and PostgreSQL cannot commit atomically. Once handed to the
        // store, an exception/cancellation may mean "committed, acknowledgement
        // lost". Preserve bytes, including rejected-operation orphans, for an
        // independent reference-aware reconciliation; never infer rollback here.
        return await service.ExecuteAsync(id, version, new Attach(new(saved.Reference, saved.Sha256, itemized)), actor, ct);
    }
}
