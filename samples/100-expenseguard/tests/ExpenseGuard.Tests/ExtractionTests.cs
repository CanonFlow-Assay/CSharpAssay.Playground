using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExpenseGuard.Infrastructure;

namespace ExpenseGuard.Tests;

public sealed class ExtractionTests
{
    private const string Draft = """
        {"merchant":"Transit","amount":42.50,"currency":"USD","spentOn":"2026-10-24","categoryGuess":"Transport","itemized":true,"proposedState":"Approved","notes":["Confirm the extracted fields."],"summary":"Advisory extraction only."}
        """;
    private static byte[] Bytes => Encoding.ASCII.GetBytes("%PDF-1.7\ncontrolled fixture");
    private static string Envelope(string draft, string status = "completed", string kind = "output_text") => JsonSerializer.Serialize(new
    {
        status, output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = kind, text = draft } } } }
    });
    [Fact] public async Task Structured_receipt_proposal_has_no_domain_authority()
    {
        using var handler = new FixtureHandler(Envelope(Draft)); using var client = new HttpClient(handler);
        var extractor = new OpenAiReceiptExtractor(client, "fixture-model", "fixture-key", 1);
        var proposal = await extractor.ExtractAsync(Bytes, "application/pdf", TestContext.Current.CancellationToken);
        Assert.Equal("Proposal", proposal.Status); Assert.Equal(42.50m, proposal.Draft!.Amount); Assert.Equal("Approved", proposal.ProposedState);
        Assert.Equal(1, handler.Calls); Assert.Equal("https://api.openai.com/v1/responses", handler.Url);
        using var payload = JsonDocument.Parse(handler.Payload);
        Assert.False(payload.RootElement.GetProperty("store").GetBoolean());
        Assert.True(payload.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.Equal("input_file", payload.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.DoesNotContain("fixture-key", handler.Payload);
        Assert.Equal("Bearer", handler.AuthScheme);
        var domain = Fixtures.Assess(Fixtures.Claim(300)); Assert.Equal("NeedsMoreInfo", ExpenseGuard.Domain.StateNames.Of(domain.State));
    }
    [Theory] [InlineData("image/png")] [InlineData("image/jpeg")]
    public async Task Images_use_inline_image_content(string contentType)
    {
        using var handler = new FixtureHandler(Envelope(Draft)); using var client = new HttpClient(handler);
        var result = await new OpenAiReceiptExtractor(client, "fixture-model", "fixture-key", 1).ExtractAsync(Bytes, contentType, TestContext.Current.CancellationToken);
        Assert.Equal("Proposal", result.Status);
        using var payload = JsonDocument.Parse(handler.Payload);
        var part = payload.RootElement.GetProperty("input")[0].GetProperty("content")[0];
        Assert.Equal("input_image", part.GetProperty("type").GetString()); Assert.StartsWith("data:" + contentType + ";base64,", part.GetProperty("image_url").GetString());
    }
    [Theory] [InlineData("incomplete", "output_text")] [InlineData("completed", "refusal")]
    public async Task Incomplete_and_refused_outputs_are_unavailable(string status, string kind)
    {
        using var handler = new FixtureHandler(Envelope(Draft, status, kind)); using var client = new HttpClient(handler);
        Assert.Equal("Unavailable", (await new OpenAiReceiptExtractor(client, "fixture-model", "fixture-key", 1).ExtractAsync(Bytes, "application/pdf", TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, handler.Calls);
    }
    [Theory] [InlineData(429)] [InlineData(401)] [InlineData(503)]
    public async Task Restrictions_and_provider_failure_are_not_retried(int status)
    {
        using var handler = new FixtureHandler(Envelope(Draft), (HttpStatusCode)status); using var client = new HttpClient(handler);
        var result = await new OpenAiReceiptExtractor(client, "fixture-model", "fixture-key", 1).ExtractAsync(Bytes, "application/pdf", TestContext.Current.CancellationToken);
        Assert.Equal("Unavailable", result.Status); Assert.Equal(1, handler.Calls);
    }
    [Theory] [InlineData("unknown-field")] [InlineData("invalid-amount")] [InlineData("invalid-state")]
    [InlineData("missing-field")] [InlineData("duplicate-field")] [InlineData("malformed")]
    [InlineData("case-alias")] [InlineData("case-duplicate")]
    public async Task Invalid_proposals_never_escape_local_validation(string kind)
    {
        var node = JsonNode.Parse(Draft)!;
        if (kind == "unknown-field") node["approveNow"] = true;
        if (kind == "invalid-amount") node["amount"] = -1;
        if (kind == "invalid-state") node["proposedState"] = "Reimbursed";
        if (kind == "missing-field") node.AsObject().Remove("merchant");
        if (kind == "case-alias") { node.AsObject().Remove("merchant"); node["Merchant"] = "Alias"; }
        if (kind == "case-duplicate") node["Merchant"] = "Alias";
        var text = kind == "malformed" ? "{" : kind == "duplicate-field" ? Draft.Replace("\"merchant\":\"Transit\"", "\"merchant\":\"A\",\"merchant\":\"B\"", StringComparison.Ordinal) : node.ToJsonString();
        using var handler = new FixtureHandler(Envelope(text)); using var client = new HttpClient(handler);
        Assert.Equal("Unavailable", (await new OpenAiReceiptExtractor(client, "fixture-model", "fixture-key", 1).ExtractAsync(Bytes, "application/pdf", TestContext.Current.CancellationToken)).Status);
    }
    [Fact] public async Task Budget_is_enforced_under_concurrent_requests()
    {
        using var handler = new FixtureHandler(Envelope(Draft)); using var client = new HttpClient(handler);
        var extractor = new OpenAiReceiptExtractor(client, "fixture-model", "fixture-key", 2);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => extractor.ExtractAsync(Bytes, "application/pdf", TestContext.Current.CancellationToken)));
        Assert.Equal(2, handler.Calls); Assert.Equal(2, results.Count(r => r.Status == "Proposal")); Assert.Equal(18, results.Count(r => r.Status == "Unavailable"));
    }
    [Fact] public async Task Oversized_response_is_unavailable()
    {
        using var handler = new FixtureHandler(new string('x', 65537)); using var client = new HttpClient(handler);
        Assert.Equal("Unavailable", (await new OpenAiReceiptExtractor(client, "fixture-model", "fixture-key", 1).ExtractAsync(Bytes, "application/pdf", TestContext.Current.CancellationToken)).Status);
    }
    [Fact] public async Task Invalid_input_never_sends_a_request()
    {
        using var handler = new FixtureHandler(Envelope(Draft)); using var client = new HttpClient(handler);
        var extractor = new OpenAiReceiptExtractor(client, "fixture-model", "fixture-key", 1);
        Assert.Equal("Unavailable", (await extractor.ExtractAsync([], "application/pdf", TestContext.Current.CancellationToken)).Status);
        Assert.Equal("Unavailable", (await extractor.ExtractAsync(Bytes, "text/html", TestContext.Current.CancellationToken)).Status);
        Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task Caller_cancellation_propagates_without_network_request()
    {
        using var handler = new FixtureHandler(Envelope(Draft)); using var client = new HttpClient(handler);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OpenAiReceiptExtractor(client, "fixture-model", "fixture-key", 1).ExtractAsync(Bytes, "application/pdf", cancelled.Token));
        Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task Disabled_adapter_has_no_network_dependency()
    {
        var adapter = new DisabledReceiptExtractor(); Assert.False(adapter.Enabled);
        var result = await adapter.ExtractAsync(Bytes, "application/pdf", TestContext.Current.CancellationToken);
        Assert.Equal("Unavailable", result.Status); Assert.Null(result.Draft);
    }
    private sealed class FixtureHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        private int calls;
        public int Calls => calls;
        public string Url { get; private set; } = "";
        public string Payload { get; private set; } = "";
        public string AuthScheme { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls); Url = request.RequestUri!.AbsoluteUri; AuthScheme = request.Headers.Authorization!.Scheme;
            Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
