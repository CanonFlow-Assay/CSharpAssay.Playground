using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ExpenseGuard.Infrastructure;

namespace ExpenseGuard.Tests;

public sealed class HttpServer : IAsyncLifetime
{
    private DatabaseScope? database;
    private Process? process;
    private Task<string>? stdout;
    private Task<string>? stderr;
    public HttpClient Client { get; } = new() { Timeout = TimeSpan.FromSeconds(20) };
    public string EmployeeKey { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public string OtherKey { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public string FinanceKey { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public string ManagerKey { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public string AuditorKey { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public string SelfManagerKey { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public string ReceiptDirectory { get; } = Path.Combine(Path.GetTempPath(), "eg-http-receipts-" + Guid.NewGuid().ToString("N"));
    private string sampleRoot = "";
    public async ValueTask InitializeAsync()
    {
        database = await DatabaseScope.CreateAsync();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "ExpenseGuard.slnx"))) root = root.Parent ?? throw new InvalidOperationException("Sample root not found.");
        sampleRoot = root.FullName;
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.Combine(root.FullName, "src/ExpenseGuard.Api") };
        start.ArgumentList.Add(Path.Combine(root.FullName, "src/ExpenseGuard.Api/bin/Release/net11.0/ExpenseGuard.Api.dll"));
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ConnectionStrings__Expenses"] = database.Connection;
        start.Environment["ExpenseGuard__Initialize"] = "false";
        start.Environment["ExpenseGuard__EnableLiveExtraction"] = "false";
        start.Environment["ExpenseGuard__ReceiptDirectory"] = ReceiptDirectory;
        start.Environment.Remove("ExpenseGuard__EvidenceRoot");
        foreach (var (index, key, employee, role) in new[] { (0, EmployeeKey, "alice", "Employee"), (1, OtherKey, "bob", "Employee"), (2, FinanceKey, "finance", "Finance"),
            (3, ManagerKey, "manager", "Manager"), (4, AuditorKey, "auditor", "Auditor"), (5, SelfManagerKey, "alice", "Manager") })
        {
            start.Environment[$"ExpenseGuard__Identities__{index}__Token"] = key;
            start.Environment[$"ExpenseGuard__Identities__{index}__Employee"] = employee;
            start.Environment[$"ExpenseGuard__Identities__{index}__Role"] = role;
        }
        process = Process.Start(start) ?? throw new InvalidOperationException("API failed to start.");
        stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
        Client.BaseAddress = new Uri($"http://127.0.0.1:{port}");
        for (var i = 0; i < 100; i++)
        {
            if (process.HasExited) throw new InvalidOperationException("API terminated before readiness: " + await stderr);
            try { using var response = await Client.GetAsync("/health", TestContext.Current.CancellationToken); if (response.IsSuccessStatusCode) return; }
            catch (HttpRequestException) { /* Startup is bounded and retried only on the local test process. */ }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("API readiness timed out.");
    }
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (process is not null)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            if (stdout is not null) await stdout;
            if (stderr is not null) await stderr;
            process.Dispose();
        }
        if (database is not null) await database.DisposeAsync();
        if (Directory.Exists(ReceiptDirectory)) Directory.Delete(ReceiptDirectory, true);
    }
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? key)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: ClaimDocument.Json);
        if (key is not null) request.Headers.Add("X-ExpenseGuard-Key", key);
        return await Client.SendAsync(request);
    }
    public async Task<JsonElement> CreateAsync(decimal amount = 300)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/claims", new { amount, currency = "USD", category = "Other",
            merchant = Guid.NewGuid().ToString("N"), spentOn = DateOnly.FromDateTime(DateTime.UtcNow), units = 1 }, EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).Clone();
    }
    public async Task<HttpResponseMessage> UploadAsync(string id, int version, byte[] bytes, string? key = null, bool itemized = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/claims/{id}/receipt-file?expectedVersion={version}&itemized={itemized}");
        request.Headers.Add("X-ExpenseGuard-Key", key ?? EmployeeKey); request.Content = new ByteArrayContent(bytes);
        return await Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
    public async Task RunWorkerUntilPaidAsync(string id)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = sampleRoot };
        start.ArgumentList.Add(Path.Combine(sampleRoot, "src/ExpenseGuard.Worker/bin/Release/net11.0/ExpenseGuard.Worker.dll"));
        start.Environment["ConnectionStrings__Expenses"] = database!.Connection;
        start.Environment["ExpenseGuard__ReceiptDirectory"] = ReceiptDirectory;
        using var worker = Process.Start(start) ?? throw new InvalidOperationException("Worker failed to start.");
        var output = worker.StandardOutput.ReadToEndAsync(); var error = worker.StandardError.ReadToEndAsync();
        try
        {
            for (var i = 0; i < 100; i++)
            {
                if (worker.HasExited) throw new InvalidOperationException("Worker terminated before processing the outbox.");
                using var response = await SendAsync(HttpMethod.Get, "/api/claims/" + id, null, EmployeeKey);
                var claim = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
                if (claim.GetProperty("claim").GetProperty("state").GetString() == "Reimbursed") return;
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            throw new InvalidOperationException("Actual worker did not record the simulated payment within its deadline.");
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(true);
            await worker.WaitForExitAsync(); await output; await error;
        }
    }
}

public sealed class HttpTests(HttpServer server) : IClassFixture<HttpServer>
{
    [Fact] public async Task Api_rejects_missing_and_invalid_identity()
    {
        using var noKey = await server.SendAsync(HttpMethod.Get, "/api/claims/" + Guid.NewGuid(), null, null);
        using var badKey = await server.SendAsync(HttpMethod.Get, "/api/claims/" + Guid.NewGuid(), null, "invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, badKey.StatusCode);
    }
    [Fact] public async Task Other_employee_cannot_read_or_mutate_claim()
    {
        var c = await server.CreateAsync(); var id = c.GetProperty("claim").GetProperty("id").GetString();
        using var get = await server.SendAsync(HttpMethod.Get, $"/api/claims/{id}/evidence", null, server.OtherKey);
        using var mutate = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 1, action = "submit" }, server.OtherKey);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode); Assert.Equal(HttpStatusCode.NotFound, mutate.StatusCode);
    }
    [Fact] public async Task Http_missing_receipt_stays_blocked_despite_advisor_approval()
    {
        var c = await server.CreateAsync(); var id = c.GetProperty("claim").GetProperty("id").GetString();
        using var submit = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 1, action = "submit" }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        using var assess = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/assess", new { expectedVersion = 2 }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, assess.StatusCode);
        using var response = await server.SendAsync(HttpMethod.Get, $"/api/claims/{id}/evidence", null, server.EmployeeKey);
        var evidence = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("NeedsMoreInfo", evidence.GetProperty("claim").GetProperty("state").GetString());
        Assert.Equal("Approved", evidence.GetProperty("aiAdvice").GetProperty("proposedState").GetString());
        Assert.True(evidence.GetProperty("audit").GetProperty("hashChainValid").GetBoolean());
        Assert.Equal("NotAvailable", evidence.GetProperty("csassay").GetProperty("verdict").GetString());
        using var payment = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 4, action = "reimburse" }, server.FinanceKey);
        Assert.Equal(HttpStatusCode.Conflict, payment.StatusCode);
    }
    [Fact] public async Task Api_does_not_accept_client_identity_or_policy_overrides()
    {
        using var response = await server.SendAsync(HttpMethod.Post, "/api/claims", new { amount = 1, currency = "USD", category = "Other",
            merchant = "x", spentOn = Fixtures.Today, units = 1, employee = "someone-else", role = "Finance" }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact] public async Task Evidence_page_has_no_inlined_sensitive_data()
    {
        using var response = await server.Client.GetAsync($"/claims/{Guid.NewGuid()}/evidence", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Follow the decision", html);
        Assert.DoesNotContain(server.EmployeeKey, html);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
