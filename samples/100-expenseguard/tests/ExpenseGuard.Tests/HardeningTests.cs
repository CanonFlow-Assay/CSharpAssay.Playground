using System.Collections.Immutable;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using ExpenseGuard.Application;
using ExpenseGuard.Domain;
using ExpenseGuard.Infrastructure;

namespace ExpenseGuard.Tests;

public sealed class HardeningTests
{
    [Theory] [InlineData("Infrastructure")] [InlineData("Application")] [InlineData("Api")] [InlineData("Worker")] [InlineData("configuration")]
    public void Changed_application_or_configuration_invalidates_evidence_with_unchanged_domain(string component)
    {
        var root = Path.Combine(Path.GetTempPath(), "eg-build-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var domain = typeof(ExpenseClaim).Assembly.Location;
            var domainHash = Hash(domain);
            var sample = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(sample.FullName, "ExpenseGuard.slnx"))) sample = sample.Parent!;
            foreach (var path in BuildEvidence.ApplicationFiles.Concat(BuildEvidence.ConfigurationFiles))
            {
                var target = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(sample.FullName, path), target);
            }
            var report = JsonSerializer.SerializeToUtf8Bytes(new { verdict = "pass", exitCode = 0,
                evidence = new { authoritative = true, verificationMode = "verification" } });
            File.WriteAllBytes(Path.Combine(root, "verify.json"), report);
            File.WriteAllText(Path.Combine(root, "build-evidence.json"), JsonSerializer.Serialize(new
            {
                schema = "expenseguard-build-binding/0.2", domainAssemblySha256 = domainHash, reportSha256 = Convert.ToHexStringLower(SHA256.HashData(report)),
                applicationAssemblySha256 = BuildEvidence.HashFiles(root, BuildEvidence.ApplicationFiles),
                configurationSha256 = BuildEvidence.HashFiles(root, BuildEvidence.ConfigurationFiles)
            }));
            Assert.True(BuildEvidence.Read(root, root).Authoritative);
            var altered = component == "configuration" ? ".csassay.json" : $"src/ExpenseGuard.{component}/bin/Release/net11.0/ExpenseGuard.{component}.dll";
            using (var changed = File.OpenWrite(Path.Combine(root, altered))) { changed.Position = changed.Length; changed.WriteByte(42); }
            Assert.Equal(domainHash, Hash(domain));
            Assert.False(BuildEvidence.Read(root, root).Authoritative);
        }
        finally { Directory.Delete(root, true); }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    [Theory] [InlineData(20)] [InlineData(50)]
    public void External_receipt_assertion_cannot_auto_approve(int amount)
    {
        var c = Fixtures.Claim(amount) with { Receipt = Fixtures.Receipt() };
        var result = PolicyEngine.Evaluate(c, Policies.Demo, Fixtures.Facts with { ReceiptBytesVerified = false });
        Assert.Equal(Decision.MoreInfo, result.Decision); Assert.Equal("VerifiedReceiptRequired", result.Evidence[^1].Code);
    }
    [Theory] [InlineData("receipt")] [InlineData("state")] [InlineData("policy")] [InlineData("rate")]
    public void Default_evidence_unions_cannot_approve(string field)
    {
        var c = Fixtures.Claim(50) with { Receipt = Fixtures.Receipt() };
        var facts = Fixtures.Facts;
        if (field == "receipt") c = c with { Receipt = default };
        if (field == "state") c = c with { State = default };
        if (field == "policy") c = c with { Policy = default };
        if (field == "rate") facts = facts with { Rate = default };
        var result = PolicyEngine.Evaluate(c, Policies.Demo, facts);
        Assert.Equal(Decision.Hold, result.Decision);
        Assert.Equal("InvalidEvidence", result.Evidence[^1].Code);
    }
    [Theory] [InlineData("receipt")] [InlineData("state")] [InlineData("policy")] [InlineData("command")]
    public void Default_unions_are_denied_at_transition_boundary(string field)
    {
        var c = Fixtures.Claim() with { State = new Draft() };
        Command command = new Submit();
        if (field == "receipt") c = c with { Receipt = default };
        if (field == "state") c = c with { State = default };
        if (field == "policy") c = c with { Policy = default };
        if (field == "command") command = default;
        Assert.Equal("InvalidEvidence", Assert.IsType<Denied>(Transitions.Decide(c, command, Fixtures.Employee).Value).Code);
    }
    [Fact] public void Default_state_name_is_explicit() => Assert.Equal("Invalid", StateNames.Of(default));
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Committed_attachment_survives_lost_acknowledgement(bool cancellation)
    {
        await using var db = await DatabaseScope.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), "eg-uncertain-upload-" + Guid.NewGuid().ToString("N"));
        try
        {
            var blobs = new ReceiptBlobStore(directory);
            var c = Fixtures.Claim(50) with { State = new Draft() };
            var created = await db.Service.CreateAsync(c, Fixtures.Employee, TestContext.Current.CancellationToken);
            var uncertain = new ExpenseService(new LostAcknowledgement(db.Store, cancellation), new PinnedRateSource([]), new DatabaseScope.FixedClock());
            var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\ncommitted-receipt");
            using var input = new MemoryStream(bytes);
            var error = await Record.ExceptionAsync(() => ReceiptAttachment.UploadAsync(blobs, uncertain, c.Id, created.Version,
                true, Fixtures.Employee, input, TestContext.Current.CancellationToken));
            if (cancellation) Assert.IsType<OperationCanceledException>(error); else Assert.IsType<IOException>(error);
            var durable = await db.Store.ReadAsync(c.Id, TestContext.Current.CancellationToken);
            Assert.Equal(2, durable!.Version); Assert.True(durable.HashChainValid);
            var receipt = Assert.IsType<Receipt>(durable.Claim.Receipt.Value);
            var content = await blobs.ReadAsync(c.Id.Value, receipt.Reference, receipt.Sha256, TestContext.Current.CancellationToken);
            Assert.NotNull(content); Assert.Equal(bytes, content.Bytes);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public async Task Missing_first_receipt_moves_to_manual_review_and_later_claim_is_paid()
    {
        await using var db = await DatabaseScope.CreateAsync();
        async Task<ExpenseClaim> Queue(string id)
        {
            var c = Fixtures.Claim(40) with { Id = new(Guid.Parse(id)), State = new Draft(), Merchant = id };
            var r = await db.Service.CreateAsync(c, Fixtures.Employee, TestContext.Current.CancellationToken);
            r = await db.AttachAsync(c, r.Version);
            r = await db.Service.ExecuteAsync(c.Id, r.Version, new Submit(), Fixtures.Employee, TestContext.Current.CancellationToken);
            r = await db.Service.AssessAsync(c.Id, r.Version, Fixtures.Employee, TestContext.Current.CancellationToken);
            Assert.IsType<Approved>(r.Claim!.State.Value);
            Assert.Empty((await db.Service.RequestPaymentAsync(c.Id, r.Version, Fixtures.Finance, TestContext.Current.CancellationToken)).Error);
            return r.Claim;
        }
        var bad = await Queue("00000000-0000-0000-0000-000000000001");
        var good = await Queue("00000000-0000-0000-0000-000000000002");
        db.Blobs.Remove(Assert.IsType<Receipt>(bad.Receipt.Value).Reference);
        var gateway = new SelectiveGateway(bad.Id);
        await Assert.ThrowsAsync<InvalidDataException>(() => db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
        var work = await db.Store.ReadPaymentWorkAsync(bad.Id, TestContext.Current.CancellationToken);
        Assert.Equal("ManualReview", work!.Status); Assert.Equal("IntegrityFailure", work.LastErrorCode); Assert.Equal(1, work.Attempts);
        Assert.True(await db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
        Assert.IsType<Reimbursed>((await db.Store.ReadAsync(good.Id, TestContext.Current.CancellationToken))!.Claim.State.Value);
        Assert.False(await db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
    }
    [Fact] public async Task Persistent_gateway_failure_stops_after_three_attempts_with_same_key()
    {
        await using var db = await DatabaseScope.CreateAsync(); var c = await db.AssessedAsync();
        await db.Service.RequestPaymentAsync(c.Claim!.Id, c.Version, Fixtures.Finance, TestContext.Current.CancellationToken);
        var gateway = new SelectiveGateway(c.Claim.Id); string? key = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await Assert.ThrowsAsync<IOException>(() => db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
            var work = await db.Store.ReadPaymentWorkAsync(c.Claim.Id, TestContext.Current.CancellationToken);
            Assert.Equal(attempt, work!.Attempts); key ??= work.IdempotencyKey; Assert.Equal(key, work.IdempotencyKey);
            Assert.Equal(attempt == 3 ? "ManualReview" : "Pending", work.Status);
            Assert.False(await db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
            db.Clock.Now = db.Clock.Now.AddMinutes(3);
        }
        Assert.False(await db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
        Assert.IsType<Approved>((await db.Store.ReadAsync(c.Claim.Id, TestContext.Current.CancellationToken))!.Claim.State.Value);
    }
    [Fact] public void Legacy_domain_only_binding_is_not_application_pass()
    {
        var root = Path.Combine(Path.GetTempPath(), "eg-legacy-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var report = JsonSerializer.SerializeToUtf8Bytes(new { verdict = "pass", exitCode = 0, evidence = new { authoritative = true, verificationMode = "verification" } });
            File.WriteAllBytes(Path.Combine(root, "verify.json"), report);
            File.WriteAllText(Path.Combine(root, "build-evidence.json"), JsonSerializer.Serialize(new
            { domainAssemblySha256 = Hash(typeof(ExpenseClaim).Assembly.Location), reportSha256 = Convert.ToHexStringLower(SHA256.HashData(report)) }));
            Assert.False(BuildEvidence.Read(root).Authoritative);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact] public async Task Failed_first_item_does_not_starve_later_payment()
    {
        await using var db = await DatabaseScope.CreateAsync();
        async Task<ExpenseClaim> Queue(string id)
        {
            var c = Fixtures.Claim(20) with { Id = new(Guid.Parse(id)), State = new Draft(), Merchant = id };
            var r = await db.Service.CreateAsync(c, Fixtures.Employee, TestContext.Current.CancellationToken);
            r = await db.Service.ExecuteAsync(c.Id, r.Version, new Submit(), Fixtures.Employee, TestContext.Current.CancellationToken);
            r = await db.Service.AssessAsync(c.Id, r.Version, Fixtures.Employee, TestContext.Current.CancellationToken);
            Assert.Empty((await db.Service.RequestPaymentAsync(c.Id, r.Version, Fixtures.Finance, TestContext.Current.CancellationToken)).Error);
            return r.Claim!;
        }
        var bad = await Queue("00000000-0000-0000-0000-000000000001");
        var good = await Queue("00000000-0000-0000-0000-000000000002");
        var gateway = new SelectiveGateway(bad.Id);
        await Assert.ThrowsAsync<IOException>(() => db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
        Assert.True(await db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
        Assert.IsType<Reimbursed>((await db.Store.ReadAsync(good.Id, TestContext.Current.CancellationToken))!.Claim.State.Value);
        Assert.IsType<Approved>((await db.Store.ReadAsync(bad.Id, TestContext.Current.CancellationToken))!.Claim.State.Value);
    }
    private sealed class SelectiveGateway(ExpenseId failing) : IPaymentGateway
    {
        public Task<string> PayAsync(ExpenseClaim claim, string idempotencyKey, CancellationToken ct) =>
            claim.Id == failing ? throw new IOException("Controlled persistent failure.") : Task.FromResult("SIMULATED-good");
    }
    private sealed class LostAcknowledgement(IExpenseStore inner, bool cancellation) : IExpenseStore
    {
        public async Task<StoreResult> ExecuteAsync(ExpenseId id, Actor actor, Func<Ledger, Mutation> decide, CancellationToken ct)
        {
            await inner.ExecuteAsync(id, actor, decide, ct);
            if (cancellation) throw new OperationCanceledException("Cancellation after durable commit.");
            throw new IOException("Commit acknowledgement deliberately lost after durable commit.");
        }
        public Task<EvidenceBundle?> ReadAsync(ExpenseId id, CancellationToken ct) => inner.ReadAsync(id, ct);
        public Task<ImmutableArray<ListedClaim>> ListAsync(Actor actor, CancellationToken ct) => inner.ListAsync(actor, ct);
    }
}
