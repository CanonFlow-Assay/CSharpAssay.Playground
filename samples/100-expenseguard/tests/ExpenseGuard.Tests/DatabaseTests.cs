using System.Collections.Immutable;
using ExpenseGuard.Application;
using ExpenseGuard.Domain;
using ExpenseGuard.Infrastructure;
using Npgsql;

namespace ExpenseGuard.Tests;

public sealed class DatabaseScope : IAsyncDisposable
{
    public string Connection { get; }
    private readonly string schema;
    private readonly string original;
    public NpgsqlDataSource Data { get; }
    public PostgresExpenseStore Store { get; }
    public ExpenseService Service { get; }
    private DatabaseScope(string connection, string source, string name)
    {
        Connection = connection; original = source; schema = name;
        Data = NpgsqlDataSource.Create(connection);
        var clock = new FixedClock();
        Store = new(Data, clock);
        Service = new(Store, new PinnedRateSource([]), clock);
    }
    public static async Task<DatabaseScope> CreateAsync()
    {
        var source = Environment.GetEnvironmentVariable("ASSAY_INVENTORY_DATABASE")
            ?? throw new InvalidOperationException("Real PostgreSQL required. Run eng/run-expenseguard.sh; no skip/fake fallback.");
        var name = "eg_" + Guid.NewGuid().ToString("N");
        await using var admin = NpgsqlDataSource.Create(source);
        await using (var create = admin.CreateCommand("CREATE SCHEMA " + name)) await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        var builder = new NpgsqlConnectionStringBuilder(source) { SearchPath = name };
        var result = new DatabaseScope(builder.ConnectionString, source, name);
        await result.Store.InitializeAsync(default);
        return result;
    }
    public async ValueTask DisposeAsync()
    {
        await Data.DisposeAsync();
        await using var admin = NpgsqlDataSource.Create(original);
        await using var command = admin.CreateCommand("DROP SCHEMA " + schema + " CASCADE");
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
    public async Task<StoreResult> AssessedAsync(decimal amount = 20m, bool receipt = false, string? merchant = null)
    {
        var c = Fixtures.Claim(amount) with { State = new Draft(), Merchant = merchant ?? Guid.NewGuid().ToString("N") };
        var r = await Service.CreateAsync(c, Fixtures.Employee, TestContext.Current.CancellationToken);
        Assert.Empty(r.Error);
        if (receipt) r = await Service.ExecuteAsync(c.Id, r.Version, new Attach(Fixtures.Receipt() with { Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N") }), Fixtures.Employee, TestContext.Current.CancellationToken);
        r = await Service.ExecuteAsync(c.Id, r.Version, new Submit(), Fixtures.Employee, TestContext.Current.CancellationToken);
        return await Service.AssessAsync(c.Id, r.Version, Fixtures.Employee, TestContext.Current.CancellationToken);
    }
    public sealed class FixedClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero); }
}

public sealed class DatabaseTests
{
    [Fact] public async Task Receipts_survive_new_connection_and_detect_tampering()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var r = await db.AssessedAsync();
        var b = await db.Store.ReadAsync(Assert.IsType<ExpenseClaim>(r.Claim).Id, TestContext.Current.CancellationToken);
        Assert.NotNull(b); Assert.True(b.HashChainValid); Assert.Equal(4, b.Version);
        Assert.Equal(new[] { "DraftCreated", "Submitted", "PolicyEvaluated", "PolicyApplied" }, b.Receipts.Select(x => x.Event));
        await using var tamper = db.Data.CreateCommand("UPDATE expense_receipts SET actor='tampered' WHERE sequence=1");
        await tamper.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        Assert.False((await db.Store.ReadAsync(b.Claim.Id, TestContext.Current.CancellationToken))!.HashChainValid);
    }
    [Fact] public async Task Optimistic_version_rejects_stale_commands_without_receipts()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var r = await db.AssessedAsync(300, true);
        var result = await db.Service.ExecuteAsync(r.Claim!.Id, r.Version - 1, new Approve(), Fixtures.Manager, TestContext.Current.CancellationToken);
        Assert.Equal("VersionConflict", result.Error);
        Assert.Equal(r.Version, (await db.Store.ReadAsync(r.Claim.Id, TestContext.Current.CancellationToken))!.Version);
    }
    [Fact] public async Task Concurrent_duplicates_cannot_both_clear()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var submitted = 0;
        async Task<StoreResult> SubmitThenAssess()
        {
            var c = Fixtures.Claim(20) with { State = new Draft(), Merchant = "same merchant" };
            var r = await db.Service.CreateAsync(c, Fixtures.Employee, TestContext.Current.CancellationToken);
            r = await db.Service.ExecuteAsync(c.Id, r.Version, new Submit(), Fixtures.Employee, TestContext.Current.CancellationToken);
            if (Interlocked.Increment(ref submitted) == 2) ready.SetResult();
            await ready.Task.WaitAsync(TestContext.Current.CancellationToken);
            return await db.Service.AssessAsync(c.Id, r.Version, Fixtures.Employee, TestContext.Current.CancellationToken);
        }
        var results = await Task.WhenAll(SubmitThenAssess(), SubmitThenAssess());
        Assert.All(results, r => { Assert.Empty(r.Error); Assert.IsType<AuditHeld>(r.Claim!.State.Value); });
    }
    [Fact] public async Task Later_match_to_approved_claim_is_held()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var first = await db.AssessedAsync(20, merchant: "same merchant");
        var later = await db.AssessedAsync(20, merchant: "same merchant");
        Assert.IsType<Approved>(first.Claim!.State.Value); Assert.IsType<AuditHeld>(later.Claim!.State.Value);
    }
    [Fact] public async Task Daily_meals_come_from_persisted_ledger()
    {
        await using var db = await DatabaseScope.CreateAsync();
        async Task<StoreResult> Meal(string merchant)
        {
            var c = Fixtures.Claim(25, Category.Meal) with { State = new Draft(), Merchant = merchant };
            var r = await db.Service.CreateAsync(c, Fixtures.Employee, TestContext.Current.CancellationToken);
            r = await db.Service.ExecuteAsync(c.Id, r.Version, new Submit(), Fixtures.Employee, TestContext.Current.CancellationToken);
            return await db.Service.AssessAsync(c.Id, r.Version, Fixtures.Employee, TestContext.Current.CancellationToken);
        }
        Assert.IsType<Approved>((await Meal("A")).Claim!.State.Value);
        Assert.IsType<Approved>((await Meal("B")).Claim!.State.Value);
        Assert.IsType<NeedsMoreInfo>((await Meal("C")).Claim!.State.Value);
    }
    [Fact] public async Task Same_receipt_hash_holds_different_merchant_and_amount()
    {
        await using var db = await DatabaseScope.CreateAsync();
        async Task<StoreResult> Expense(decimal amount, string merchant)
        {
            var c = Fixtures.Claim(amount) with { State = new Draft(), Merchant = merchant };
            var r = await db.Service.CreateAsync(c, Fixtures.Employee, TestContext.Current.CancellationToken);
            r = await db.Service.ExecuteAsync(c.Id, r.Version, new Attach(Fixtures.Receipt()), Fixtures.Employee, TestContext.Current.CancellationToken);
            r = await db.Service.ExecuteAsync(c.Id, r.Version, new Submit(), Fixtures.Employee, TestContext.Current.CancellationToken);
            return await db.Service.AssessAsync(c.Id, r.Version, Fixtures.Employee, TestContext.Current.CancellationToken);
        }
        Assert.IsType<Approved>((await Expense(10, "merchant A")).Claim!.State.Value);
        Assert.IsType<AuditHeld>((await Expense(20, "merchant B")).Claim!.State.Value);
    }
    [Fact] public async Task Cancellation_before_command_does_not_change_claim()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var r = await db.AssessedAsync(300, true);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Service.ExecuteAsync(r.Claim!.Id, r.Version,
            new Approve(), Fixtures.Manager, cancelled.Token));
        var after = await db.Store.ReadAsync(r.Claim!.Id, TestContext.Current.CancellationToken);
        Assert.Equal(r.Version, after!.Version); Assert.True(after.HashChainValid);
    }
    [Fact] public async Task Reimbursement_is_idempotent_and_has_durable_receipt()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var r = await db.AssessedAsync();
        var queued = await db.Service.RequestPaymentAsync(r.Claim!.Id, r.Version, Fixtures.Finance, TestContext.Current.CancellationToken);
        Assert.Empty(queued.Error);
        Assert.Equal("PaymentAlreadyRequested", (await db.Service.RequestPaymentAsync(r.Claim.Id, queued.Version, Fixtures.Finance, TestContext.Current.CancellationToken)).Error);
        var gateway = new CountingGateway();
        Assert.True(await db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
        Assert.False(await db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
        Assert.Equal(1, gateway.Calls);
        var b = await db.Store.ReadAsync(r.Claim.Id, TestContext.Current.CancellationToken);
        Assert.IsType<Reimbursed>(b!.Claim.State.Value); Assert.True(b.HashChainValid);
    }
    [Fact] public async Task Audit_hold_before_send_cancels_outbox()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var r = await db.AssessedAsync();
        var q = await db.Service.RequestPaymentAsync(r.Claim!.Id, r.Version, Fixtures.Finance, TestContext.Current.CancellationToken);
        Assert.Empty((await db.Service.ExecuteAsync(r.Claim.Id, q.Version, new Hold("investigate"), Fixtures.Auditor, TestContext.Current.CancellationToken)).Error);
        var gateway = new CountingGateway();
        Assert.True(await db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
        Assert.Equal(0, gateway.Calls);
        Assert.IsType<AuditHeld>((await db.Store.ReadAsync(r.Claim.Id, TestContext.Current.CancellationToken))!.Claim.State.Value);
    }
    [Fact] public async Task Gateway_failure_rolls_back_and_retries_same_key()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var r = await db.AssessedAsync();
        await db.Service.RequestPaymentAsync(r.Claim!.Id, r.Version, Fixtures.Finance, TestContext.Current.CancellationToken);
        var gateway = new CountingGateway { Fail = true };
        await Assert.ThrowsAsync<IOException>(() => db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
        Assert.IsType<Approved>((await db.Store.ReadAsync(r.Claim.Id, TestContext.Current.CancellationToken))!.Claim.State.Value);
        var key = gateway.LastKey;
        gateway.Fail = false;
        Assert.True(await db.Store.ProcessOneAsync(gateway, TestContext.Current.CancellationToken));
        Assert.Equal(key, gateway.LastKey);
    }
    [Fact] public async Task Failed_audit_insert_rolls_back_state()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var r = await db.AssessedAsync(300, true);
        await using (var breakAudit = db.Data.CreateCommand("ALTER TABLE expense_receipts ADD CONSTRAINT reject_manager CHECK(event <> 'ManagerApproved')")) await breakAudit.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<PostgresException>(() => db.Service.ExecuteAsync(r.Claim!.Id, r.Version, new Approve(), Fixtures.Manager, TestContext.Current.CancellationToken));
        var b = await db.Store.ReadAsync(r.Claim!.Id, TestContext.Current.CancellationToken);
        Assert.Equal(r.Version, b!.Version); Assert.IsType<ManagerApprovalRequired>(b.Claim.State.Value); Assert.True(b.HashChainValid);
    }
    [Fact] public async Task Advice_has_no_state_authority()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var r = await db.AssessedAsync(300);
        var advice = await new DemoAdvisor().SuggestAsync(r.Claim!, TestContext.Current.CancellationToken);
        Assert.Equal("Approved", advice.ProposedState);
        Assert.IsType<NeedsMoreInfo>((await db.Store.ReadAsync(r.Claim!.Id, TestContext.Current.CancellationToken))!.Claim.State.Value);
    }
    [Fact] public async Task Caller_cannot_inject_check_facts_or_payment_receipt()
    {
        await using var db = await DatabaseScope.CreateAsync();
        var r = await db.AssessedAsync(300);
        Assert.Equal("InternalCommand", (await db.Service.ExecuteAsync(r.Claim!.Id, r.Version, new Check(Policies.Demo, Fixtures.Facts), Fixtures.Employee, TestContext.Current.CancellationToken)).Error);
        Assert.Equal("InternalCommand", (await db.Service.ExecuteAsync(r.Claim.Id, r.Version, new Reimburse("fake"), Fixtures.Employee, TestContext.Current.CancellationToken)).Error);
    }
    private sealed class CountingGateway : IPaymentGateway
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public string LastKey { get; private set; } = "";
        public Task<string> PayAsync(ExpenseClaim claim, string idempotencyKey, CancellationToken ct)
        {
            Calls++; LastKey = idempotencyKey;
            if (Fail) throw new IOException("Controlled gateway failure.");
            return Task.FromResult("SIMULATED-payment");
        }
    }
}
