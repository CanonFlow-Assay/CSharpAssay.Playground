using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpenseGuard.Application;
using ExpenseGuard.Domain;
using ExpenseGuard.Infrastructure;

namespace ExpenseGuard.Tests;

public sealed class ProductHttpTests(HttpServer server) : IClassFixture<HttpServer>
{
    private static byte[] ReceiptBytes() => Encoding.ASCII.GetBytes("%PDF-1.7\nfixture-" + Guid.NewGuid().ToString("N"));
    [Theory] [InlineData("category")] [InlineData("amount")] [InlineData("spentOn")] [InlineData("numeric-category")]
    public async Task Required_claim_fields_cannot_acquire_implicit_defaults(string field)
    {
        var body = new Dictionary<string, object> { ["amount"] = 20m, ["currency"] = "USD", ["category"] = "Other",
            ["merchant"] = Guid.NewGuid().ToString("N"), ["spentOn"] = Fixtures.Today, ["units"] = 1 };
        if (field == "numeric-category") body["category"] = 0; else body.Remove(field);
        using var response = await server.SendAsync(HttpMethod.Post, "/api/claims", body, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact] public async Task Complete_http_review_and_actual_worker_flow()
    {
        var created = await server.CreateAsync(1200); var id = created.GetProperty("claim").GetProperty("id").GetString()!;
        using var upload = await server.UploadAsync(id, 1, ReceiptBytes()); Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var submitted = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 2, action = "submit" }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        using var assessed = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/assess", new { expectedVersion = 3 }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, assessed.StatusCode);
        Assert.Equal("ManagerApprovalRequired", (await assessed.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("claim").GetProperty("state").GetString());
        using var self = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 5, action = "approve" }, server.SelfManagerKey);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        using var manager = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 5, action = "approve" }, server.ManagerKey);
        Assert.Equal(HttpStatusCode.OK, manager.StatusCode);
        Assert.Equal("FinanceApprovalRequired", (await manager.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("claim").GetProperty("state").GetString());
        using var finance = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 6, action = "approve" }, server.FinanceKey);
        Assert.Equal(HttpStatusCode.OK, finance.StatusCode);
        using var queue = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 7, action = "reimburse" }, server.FinanceKey);
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        await server.RunWorkerUntilPaidAsync(id);
        using var evidence = await server.SendAsync(HttpMethod.Get, $"/api/claims/{id}/evidence", null, server.EmployeeKey);
        var receipt = await evidence.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Reimbursed", receipt.GetProperty("claim").GetProperty("state").GetString());
        Assert.StartsWith("SIMULATED-", receipt.GetProperty("claim").GetProperty("stateDetail").GetString());
        Assert.True(receipt.GetProperty("audit").GetProperty("hashChainValid").GetBoolean());
        Assert.Equal("VerifiedHash", receipt.GetProperty("receiptStorage").GetString());
        Assert.Single(receipt.GetProperty("audit").GetProperty("receipts").EnumerateArray(), r => r.GetProperty("event").GetString() == "Reimbursed");
    }
    [Fact] public async Task Receipt_roundtrip_is_private_and_server_hashed()
    {
        var created = await server.CreateAsync(); var id = created.GetProperty("claim").GetProperty("id").GetString()!; var bytes = ReceiptBytes();
        using var upload = await server.UploadAsync(id, 1, bytes); Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var stored = await upload.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), stored.GetProperty("claim").GetProperty("receipt").GetProperty("sha256").GetString());
        using var owner = await server.SendAsync(HttpMethod.Get, $"/api/claims/{id}/receipt-file", null, server.EmployeeKey);
        Assert.Equal(bytes, await owner.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("attachment", owner.Content.Headers.ContentDisposition!.DispositionType);
        using var other = await server.SendAsync(HttpMethod.Get, $"/api/claims/{id}/receipt-file", null, server.OtherKey);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }
    [Fact] public async Task Revise_clears_current_receipt_but_authorized_audit_download_retains_old_bytes()
    {
        var created = await server.CreateAsync(); var id = created.GetProperty("claim").GetProperty("id").GetString()!; var bytes = ReceiptBytes();
        using var upload = await server.UploadAsync(id, 1, bytes); Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var revise = await server.SendAsync(HttpMethod.Put, $"/api/claims/{id}", new { expectedVersion = 2, amount = 200, currency = "USD", category = "Hotel", merchant = "Revised", spentOn = Fixtures.Today, units = 1 }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, revise.StatusCode);
        var c = await revise.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(JsonValueKind.Null, c.GetProperty("claim").GetProperty("receipt").ValueKind);
        using var current = await server.SendAsync(HttpMethod.Get, $"/api/claims/{id}/receipt-file", null, server.EmployeeKey);
        using var historical = await server.SendAsync(HttpMethod.Get, $"/api/claims/{id}/receipt-file?sequence=2", null, server.AuditorKey);
        Assert.Equal(HttpStatusCode.NotFound, current.StatusCode); Assert.Equal(HttpStatusCode.OK, historical.StatusCode);
        Assert.Equal(bytes, await historical.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }
    [Fact] public async Task Invalid_and_stale_uploads_are_rejected()
    {
        var c = await server.CreateAsync(); var id = c.GetProperty("claim").GetProperty("id").GetString()!;
        using var invalid = await server.UploadAsync(id, 1, Encoding.ASCII.GetBytes("not a receipt"));
        using var large = await server.UploadAsync(id, 1, new byte[ReceiptBlobStore.MaximumBytes + 1]);
        using var stale = await server.UploadAsync(id, 0, ReceiptBytes());
        using var wrongOwner = await server.UploadAsync(id, 1, ReceiptBytes(), server.OtherKey);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal(HttpStatusCode.NotFound, wrongOwner.StatusCode);
    }
    [Fact] public async Task Claim_listing_and_identity_are_scoped_to_the_authenticated_actor()
    {
        var c = await server.CreateAsync(); var id = c.GetProperty("claim").GetProperty("id").GetString();
        using var me = await server.SendAsync(HttpMethod.Get, "/api/me", null, server.EmployeeKey);
        var profile = await me.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("alice", profile.GetProperty("employee").GetString()); Assert.False(profile.GetProperty("extractionEnabled").GetBoolean());
        using var own = await server.SendAsync(HttpMethod.Get, "/api/claims", null, server.EmployeeKey);
        using var other = await server.SendAsync(HttpMethod.Get, "/api/claims", null, server.OtherKey);
        Assert.Contains((await own.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).EnumerateArray(), r => r.GetProperty("claim").GetProperty("id").GetString() == id);
        Assert.DoesNotContain((await other.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).EnumerateArray(), r => r.GetProperty("claim").GetProperty("id").GetString() == id);
    }
    [Fact] public async Task Disabled_extraction_changes_neither_state_nor_version()
    {
        var c = await server.CreateAsync(); var id = c.GetProperty("claim").GetProperty("id").GetString()!;
        using var response = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/receipt-extraction", new { expectedVersion = 1, confirmExternalDisclosure = false }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proposal = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Unavailable", proposal.GetProperty("proposal").GetProperty("status").GetString()); Assert.Equal(1, proposal.GetProperty("version").GetInt32());
    }
    [Fact] public async Task Tampered_upload_cannot_be_downloaded_or_submitted()
    {
        var c = await server.CreateAsync(); var id = c.GetProperty("claim").GetProperty("id").GetString()!;
        using var upload = await server.UploadAsync(id, 1, ReceiptBytes()); Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var path = Directory.GetFiles(server.ReceiptDirectory, Guid.Parse(id).ToString("N") + "_*.blob").Single();
        await File.WriteAllTextAsync(path, "%PDF-1.7\ntampered", TestContext.Current.CancellationToken);
        using var download = await server.SendAsync(HttpMethod.Get, $"/api/claims/{id}/receipt-file", null, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.Conflict, download.StatusCode);
        using var submit = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 2, action = "submit" }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.InternalServerError, submit.StatusCode);
        using var evidence = await server.SendAsync(HttpMethod.Get, $"/api/claims/{id}/evidence", null, server.EmployeeKey);
        var state = await evidence.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Draft", state.GetProperty("claim").GetProperty("state").GetString()); Assert.Equal("Invalid", state.GetProperty("receiptStorage").GetString());
    }
}

public sealed class ProductDatabaseTests
{
    [Fact] public async Task Corrupt_audit_blocks_future_mutation()
    {
        await using var db = await DatabaseScope.CreateAsync(); var result = await db.AssessedAsync(300, true);
        await using (var tamper = db.Data.CreateCommand("UPDATE expense_receipts SET actor='tampered' WHERE sequence=1")) await tamper.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => db.Service.ExecuteAsync(result.Claim!.Id, result.Version, new Approve(), Fixtures.Manager, TestContext.Current.CancellationToken));
        var observed = await db.Store.ReadAsync(result.Claim!.Id, TestContext.Current.CancellationToken);
        Assert.Equal(result.Version, observed!.Version); Assert.False(observed.HashChainValid);
    }
    [Fact] public async Task Corrupt_audit_blocks_payment_gateway()
    {
        await using var db = await DatabaseScope.CreateAsync(); var result = await db.AssessedAsync();
        await db.Service.RequestPaymentAsync(result.Claim!.Id, result.Version, Fixtures.Finance, TestContext.Current.CancellationToken);
        await using (var tamper = db.Data.CreateCommand("UPDATE expense_receipts SET actor='tampered' WHERE sequence=1")) await tamper.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        var gateway = new RecordingGateway();
        await Assert.ThrowsAsync<InvalidDataException>(() => db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
        Assert.Equal(0, gateway.Calls);
    }
    [Fact] public async Task Payment_does_not_send_when_receipt_file_missing()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), "eg-payment-receipt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var blobs = new ReceiptBlobStore(directory); var clock = new DatabaseScope.FixedClock();
            var store = new PostgresExpenseStore(db.Data, clock, blobs); var service = new ExpenseService(store, new PinnedRateSource([]), clock);
            var c = Fixtures.Claim(40) with { State = new Draft() };
            var r = await service.CreateAsync(c, Fixtures.Employee, TestContext.Current.CancellationToken);
            using var input = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nfixture"));
            var file = await blobs.SaveAsync(c.Id.Value, input, TestContext.Current.CancellationToken);
            r = await service.ExecuteAsync(c.Id, r.Version, new Attach(new(file.Reference, file.Sha256, true)), Fixtures.Employee, TestContext.Current.CancellationToken);
            r = await service.ExecuteAsync(c.Id, r.Version, new Submit(), Fixtures.Employee, TestContext.Current.CancellationToken);
            r = await service.AssessAsync(c.Id, r.Version, Fixtures.Employee, TestContext.Current.CancellationToken);
            await service.RequestPaymentAsync(c.Id, r.Version, Fixtures.Finance, TestContext.Current.CancellationToken);
            blobs.Remove(file.Reference); var gateway = new RecordingGateway();
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken)); Assert.Equal(0, gateway.Calls);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class RecordingGateway : IPaymentGateway
    {
        public int Calls { get; private set; }
        public Task<string> PayAsync(ExpenseClaim claim, string idempotencyKey, CancellationToken ct) { Calls++; return Task.FromResult("SIMULATED-payment"); }
    }
}
