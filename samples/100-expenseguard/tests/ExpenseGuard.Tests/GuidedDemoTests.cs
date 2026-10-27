using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace ExpenseGuard.Tests;

public sealed class GuidedDemoTests(HttpServer server) : IClassFixture<HttpServer>
{
    [Theory] [InlineData("24.99", "Approved", "D01-below")] [InlineData("25", "Approved", "D01-at")] [InlineData("25.01", "NeedsMoreInfo", "D01")]
    public async Task Missing_receipt_threshold_is_domain_policy_not_advisor_authority(string amountText, string expected, string id)
    {
        var amount = decimal.Parse(amountText, CultureInfo.InvariantCulture);
        var created = await server.CreateAsync(amount); var claim = created.GetProperty("claim").GetProperty("id").GetString();
        using var submitted = await server.SendAsync(HttpMethod.Post, $"/api/claims/{claim}/commands", new { expectedVersion = 1, action = "submit" }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        using var assessed = await server.SendAsync(HttpMethod.Post, $"/api/claims/{claim}/assess", new { expectedVersion = 2 }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, assessed.StatusCode);
        using var response = await server.SendAsync(HttpMethod.Get, $"/api/claims/{claim}/evidence", null, server.EmployeeKey);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var actual = result.GetProperty("claim").GetProperty("state").GetString();
        var advice = result.GetProperty("aiAdvice").GetProperty("proposedState").GetString();
        Assert.Equal(expected, actual); Assert.Equal("Approved", advice);
        Observe(id, "Missing receipt: below, at and above threshold", $"USD {amount}; relative current date; Other; no duplicate; missing receipt", expected,
            new { detector = "ExpenseGuard domain through authenticated HTTP + PostgreSQL", state = actual, advisor = advice, simulated = true, httpStatus = (int)assessed.StatusCode },
            "Business behavior asserted by tests; not inferred by Roslyn. Default advisor is simulated. No real payment or model request.");
    }

    [Fact] public async Task External_assertion_then_stored_upload_has_different_evidence()
    {
        var c = await server.CreateAsync(50); var id = c.GetProperty("claim").GetProperty("id").GetString()!;
        using var attach = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/receipt", new { expectedVersion = 1, reference = "receipt:guided-assertion", sha256 = new string('a', 64), itemized = true }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, attach.StatusCode);
        using var submit = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 2, action = "submit" }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        using var assess = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/assess", new { expectedVersion = 3 }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, assess.StatusCode);
        var asserted = await assess.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("NeedsMoreInfo", asserted.GetProperty("claim").GetProperty("state").GetString());
        Assert.Equal("VerifiedReceiptRequired", asserted.GetProperty("claim").GetProperty("stateDetail").GetString());
        using var upload = await server.UploadAsync(id, 5, Encoding.ASCII.GetBytes("%PDF-1.7\nguided-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var resubmit = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 6, action = "submit" }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, resubmit.StatusCode);
        using var reassess = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/assess", new { expectedVersion = 7 }, server.EmployeeKey);
        Assert.Equal(HttpStatusCode.OK, reassess.StatusCode);
        var stored = await reassess.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Approved", stored.GetProperty("claim").GetProperty("state").GetString());
        Observe("D02", "An external assertion is not verified stored bytes", "USD 50, expense-v2, asserted reference/hash then synthetic stored PDF", "NeedsMoreInfo before upload; Approved after upload/reassessment",
            new { detector = "Domain policy plus server-side receipt hashing through real HTTP/PostgreSQL", asserted = "NeedsMoreInfo", uploaded = "Approved", policy = "expense-v2" },
            "Stored bytes/hash do not prove receipt authenticity, malware safety or itemization. External URLs were not fetched.");
    }

    [Fact] public async Task Self_approval_is_rejected_through_normal_authenticated_API()
    {
        var c = await server.CreateAsync(300); var id = c.GetProperty("claim").GetProperty("id").GetString()!;
        using var upload = await server.UploadAsync(id, 1, Encoding.ASCII.GetBytes("%PDF-1.7\nguided-" + Guid.NewGuid().ToString("N"))); Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var submit = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 2, action = "submit" }, server.EmployeeKey); Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        using var assess = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/assess", new { expectedVersion = 3 }, server.EmployeeKey); Assert.Equal(HttpStatusCode.OK, assess.StatusCode);
        using var denied = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 5, action = "approve" }, server.SelfManagerKey);
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        using var allowed = await server.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 5, action = "approve" }, server.ManagerKey); Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var approved = await allowed.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken); Assert.Equal("Approved", approved.GetProperty("claim").GetProperty("state").GetString());
        Observe("D03", "Approval needs the appropriate distinct actor", "USD 300 with stored synthetic receipt; claimant also has a manager identity", "Self-approval conflict; distinct manager can approve",
            new { detector = "Domain transition authorization through real authenticated API/PostgreSQL", selfApprovalHttp = 409, distinctManagerHttp = 200 },
            "This tests the sample's server-configured identities. It is not enterprise identity, tenant isolation or an analyzer-inferred authorization rule.");
    }

    [Fact] public async Task Recorded_guide_does_not_enable_unauthenticated_claim_access()
    {
        using var page = await server.Client.GetAsync("/?demo=recorded", TestContext.Current.CancellationToken); Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("?demo=recorded", page.RequestMessage?.RequestUri?.Query);
        using var claims = await server.SendAsync(HttpMethod.Get, "/api/claims", null, null); Assert.Equal(HttpStatusCode.Unauthorized, claims.StatusCode);
    }

    [Fact] public async Task Paired_advisory_recommendations_cannot_change_domain_or_authorize_payment()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow); var observations = new List<object>();
        foreach (var recommendation in new[] { "Approved", "Rejected" })
        {
            var isolated = new HttpServer { DemoRecommendation = recommendation }; await isolated.InitializeAsync();
            try
            {
                using var created = await isolated.SendAsync(HttpMethod.Post, "/api/claims", new { amount = 26m, currency = "USD", category = "Other", merchant = "guided-paired-advice", spentOn = today, units = 1 }, isolated.EmployeeKey);
                Assert.Equal(HttpStatusCode.OK, created.StatusCode);
                var claim = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken); var id = claim.GetProperty("claim").GetProperty("id").GetString();
                using var submit = await isolated.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 1, action = "submit" }, isolated.EmployeeKey); Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
                using var assess = await isolated.SendAsync(HttpMethod.Post, $"/api/claims/{id}/assess", new { expectedVersion = 2 }, isolated.EmployeeKey); Assert.Equal(HttpStatusCode.OK, assess.StatusCode);
                using var response = await isolated.SendAsync(HttpMethod.Get, $"/api/claims/{id}/evidence", null, isolated.EmployeeKey);
                var result = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
                Assert.Equal(recommendation, result.GetProperty("aiAdvice").GetProperty("proposedState").GetString());
                Assert.Equal("NeedsMoreInfo", result.GetProperty("claim").GetProperty("state").GetString());
                using var payment = await isolated.SendAsync(HttpMethod.Post, $"/api/claims/{id}/commands", new { expectedVersion = 4, action = "reimburse" }, isolated.FinanceKey);
                Assert.Equal(HttpStatusCode.Conflict, payment.StatusCode);
                observations.Add(new { advisor = recommendation, domain = "NeedsMoreInfo", paymentHttpStatus = (int)payment.StatusCode });
            }
            finally { await isolated.DisposeAsync(); }
        }
        Observe("D01-advice-invariance", "Advice can change without changing authority", "Same policy-relevant facts: USD 26, Other, merchant, one captured current date, missing receipt, no duplicate; separate isolated ledgers", "Both recommendations retain NeedsMoreInfo and payment conflict",
            new { detector = "Actual configured offline advisor + authenticated HTTP/PostgreSQL + payment-request guard", pairs = observations },
            "Claim IDs differ between isolated ledgers; policy-relevant facts match. Offline fixtures, no model request or payment.");
    }

    private static void Observe(string id, string title, string input, string expected, object observation, string limits)
    {
        var output = Environment.GetEnvironmentVariable("ASSAY_GUIDED_OBSERVATIONS"); if (output is null) return;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, id + ".json"), JsonSerializer.Serialize(new { id, kind = "scenario", title, input, expected, observation, limits, status = "Recorded example",
            execution = new { state = "Completed", command = "dotnet ExpenseGuard.Tests.dll --filter-class ExpenseGuard.Tests.GuidedDemoTests", clock = "Current UTC date supplied at execution; no expiring fixed date" } }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
