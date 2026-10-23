using System.Net;
using System.Text;
using Playground.DecisionAssist.Cli;
using Playground.DecisionAssist.Domain;
using Xunit;

namespace Playground.Tests;

public sealed class DecisionAssistanceTests
{
    private static MutationEvidence Survivor(VerificationVerdict verdict = VerificationVerdict.Fail) =>
        new(new string('a', 64), new SurvivingMutation(), Observation.Unknown, Observation.Absent, verdict);

    [Fact]
    public void Deterministic_baseline_identifies_the_declared_missing_assertion()
    {
        var result = Assistance.Baseline(Survivor());
        Assert.Equal(NextAction.InspectAssertion, Assert.IsType<Recommendation>(result.Value).Action);
    }

    [Fact]
    public void Provider_state_preserves_order_context_without_verification_authority()
    {
        var evidence = Survivor() with
        {
            Task = "functional-shape-order-investigation",
            InvestigationContext = "Invalid quantity must reject the order before storage."
        };
        using var document = System.Text.Json.JsonSerializer.SerializeToDocument(ProviderAdvice.State(evidence));
        Assert.Equal(evidence.Task, document.RootElement.GetProperty("task").GetString());
        Assert.Equal(evidence.InvestigationContext, document.RootElement.GetProperty("investigationContext").GetString());
        Assert.False(document.RootElement.TryGetProperty("verificationVerdict", out _));
        Assert.Equal(VerificationVerdict.Fail, evidence.VerificationVerdict);
        Assert.DoesNotContain("zero-boundary", Assistance.Explain(Assistance.Baseline(evidence), FeedbackTone.Coaching), StringComparison.Ordinal);
    }

    [Fact]
    public void No_coverage_takes_priority_over_a_suspected_assertion_gap()
    {
        var result = Assistance.Baseline(Survivor() with { Covered = Observation.Absent });
        Assert.Equal(NextAction.InspectReachability, Assert.IsType<Recommendation>(result.Value).Action);
    }

    [Fact]
    public void Unknown_context_requests_human_review_before_model_advice()
    {
        var result = Assistance.Baseline(Survivor() with { HasBoundaryAssertion = Observation.Unknown });
        Assert.IsType<ReviewRequired>(result.Value);
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void Incomplete_or_irrelevant_experiments_cannot_get_provider_recommendations(
        bool baseline, bool compiled, bool completed, bool survived)
    {
        var evidence = Survivor() with { State = Assistance.Observe(baseline, compiled, completed, survived) };
        Assert.IsType<ReviewRequired>(Assistance.Suggest(evidence, NextAction.InspectAssertion).Value);
    }

    [Theory]
    [InlineData(VerificationVerdict.Pass)]
    [InlineData(VerificationVerdict.Fail)]
    [InlineData(VerificationVerdict.Inconclusive)]
    [InlineData(VerificationVerdict.ToolFailure)]
    public void Advice_and_feedback_tones_leave_the_verification_verdict_unchanged(VerificationVerdict verdict)
    {
        var evidence = Survivor(verdict);
        var advice = Assistance.Suggest(evidence, NextAction.ReviewContract);
        var concise = Assistance.Explain(advice, FeedbackTone.Concise);
        var coaching = Assistance.Explain(advice, FeedbackTone.Coaching);
        var firm = Assistance.Explain(advice, FeedbackTone.Firm);
        Assert.Equal(verdict, evidence.VerificationVerdict);
        Assert.Equal(NextAction.ReviewContract, Assert.IsType<Recommendation>(advice.Value).Action);
        Assert.NotEqual(concise, coaching);
        Assert.StartsWith("Attention:", firm, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("inspect_assertion", NextAction.InspectAssertion)]
    [InlineData("inspect_reachability", NextAction.InspectReachability)]
    [InlineData("review_contract", NextAction.ReviewContract)]
    public void Adapter_accepts_only_registered_investigations(string choice, NextAction expected)
    {
        var json = "{\"answers\":{\"next\":{\"type\":\"choice\",\"choice\":\"" + choice + "\"}}}";
        var result = JevAdvice.Parse(Survivor(), Encoding.UTF8.GetBytes(json));
        Assert.Equal(expected, Assert.IsType<Recommendation>(result.Value).Action);
    }

    [Fact]
    public void Jev_human_review_maps_to_review_status_like_other_providers()
    {
        var json = Encoding.UTF8.GetBytes("{\"answers\":{\"next\":{\"type\":\"choice\",\"choice\":\"human_review\"}}}");
        Assert.IsType<ReviewRequired>(JevAdvice.Parse(Survivor(), json).Value);
    }

    [Theory]
    [InlineData("{\"answers\":{\"next\":{\"choice\":\"Pass\"}}}")]
    [InlineData("{\"answers\":{\"next\":{\"choice\":\"ignore_survivor\"}}}")]
    [InlineData("{\"answers\":{\"next\":{\"type\":\"refusal\"}}}")]
    public void Refusal_or_unregistered_action_requires_review(string response)
    {
        Assert.IsType<ReviewRequired>(JevAdvice.Parse(Survivor(), Encoding.UTF8.GetBytes(response)).Value);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"answers\":[]}")]
    [InlineData("{\"answers\":{\"next\":{\"choice\":false}}}")]
    public void Malformed_response_is_unavailable_not_a_verdict(string response)
    {
        Assert.IsType<AdviceUnavailable>(JevAdvice.Parse(Survivor(), Encoding.UTF8.GetBytes(response)).Value);
    }

    [Fact]
    public async Task Missing_key_never_sends_a_request()
    {
        var requests = 0;
        using var handler = new ResponseHandler(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.OK); });
        using var client = new HttpClient(handler);
        Assert.IsType<AdviceUnavailable>((await JevAdvice.RequestAsync(client, Survivor(), string.Empty, CancellationToken.None)).Value);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Provider_failure_is_attempted_once_and_cannot_change_verdict(HttpStatusCode status)
    {
        var requests = 0;
        using var handler = new ResponseHandler(_ => { requests++; return new HttpResponseMessage(status); });
        using var client = new HttpClient(handler);
        var evidence = Survivor();
        Assert.IsType<AdviceUnavailable>((await JevAdvice.RequestAsync(client, evidence, "synthetic-key", CancellationToken.None)).Value);
        Assert.Equal(1, requests);
        Assert.Equal(VerificationVerdict.Fail, evidence.VerificationVerdict);
    }

    [Fact]
    public async Task Transport_failure_retains_a_nonpassing_verdict()
    {
        using var handler = new ResponseHandler(_ => throw new HttpRequestException("Synthetic outage"));
        using var client = new HttpClient(handler);
        Assert.IsType<AdviceUnavailable>((await JevAdvice.RequestAsync(client, Survivor(), "synthetic-key", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task Live_adapter_encodes_only_synthetic_metadata_and_the_registered_question()
    {
        var body = string.Empty;
        var endpoint = string.Empty;
        using var handler = new ResponseHandler(request =>
        {
            endpoint = Assert.IsType<Uri>(request.RequestUri).AbsoluteUri;
            body = Assert.IsAssignableFrom<HttpContent>(request.Content).ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"answers\":{\"next\":{\"type\":\"choice\",\"choice\":\"inspect_assertion\"}}}")
            };
        });
        using var client = new HttpClient(handler);
        var evidence = Survivor();
        var advice = await JevAdvice.RequestAsync(client, evidence, "synthetic-key", CancellationToken.None);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", endpoint);
        using var requestBody = System.Text.Json.JsonDocument.Parse(body);
        var state = requestBody.RootElement.GetProperty("state");
        Assert.Equal("Unknown", state.GetProperty("covered").GetString());
        Assert.Equal("Absent", state.GetProperty("hasBoundaryAssertion").GetString());
        Assert.False(state.TryGetProperty("verificationVerdict", out _));
        Assert.Equal(4, requestBody.RootElement.GetProperty("questions").GetProperty("next").GetProperty("criteria").EnumerateObject().Count());
        Assert.Equal(NextAction.InspectAssertion, Assert.IsType<Recommendation>(advice.Value).Action);
        Assert.Equal(VerificationVerdict.Fail, evidence.VerificationVerdict);
    }

    [Fact]
    public async Task Oversized_provider_output_is_rejected()
    {
        using var handler = new ResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 65537))
        });
        using var client = new HttpClient(handler);
        Assert.IsType<AdviceUnavailable>((await JevAdvice.RequestAsync(client, Survivor(), "synthetic-key", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task Cancellation_is_unavailable_advice()
    {
        using var handler = new ResponseHandler(_ => throw new OperationCanceledException());
        using var client = new HttpClient(handler);
        Assert.IsType<AdviceUnavailable>((await JevAdvice.RequestAsync(client, Survivor(), "synthetic-key", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task Invalid_credential_is_not_sent_or_echoed()
    {
        var requests = 0;
        using var handler = new ResponseHandler(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.OK); });
        using var client = new HttpClient(handler);
        var advice = await JevAdvice.RequestAsync(client, Survivor(), "synthetic\nkey", CancellationToken.None);
        Assert.IsType<AdviceUnavailable>(advice.Value);
        Assert.Equal(0, requests);
        Assert.DoesNotContain("synthetic", Assistance.Explain(advice, FeedbackTone.Concise), StringComparison.Ordinal);
    }

    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
