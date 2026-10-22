using System.Net;
using System.Text;
using System.Text.Json;
using Playground.DecisionAssist.Cli;
using Playground.DecisionAssist.Domain;
using Xunit;

namespace Playground.Tests;

public sealed class ProviderMappingTests
{
    private static MutationEvidence Evidence() => new(new string('b', 64), new SurvivingMutation(), Observation.Unknown, Observation.Absent, VerificationVerdict.Fail);

    [Theory]
    [InlineData(AdviceProtocol.Decisions, "{\"answers\":[{\"name\":\"next\",\"type\":\"choice\",\"choice\":\"inspect_assertion\"}]}")]
    [InlineData(AdviceProtocol.SystemOne, "{\"answers\":{\"next\":{\"type\":\"choice\",\"choice\":\"inspect_assertion\"}}}")]
    [InlineData(AdviceProtocol.Cloudflare, "{\"success\":true,\"result\":{\"answers\":{\"next\":{\"type\":\"choice\",\"choice\":\"inspect_assertion\"}}}}")]
    [InlineData(AdviceProtocol.Chat, "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"{\\\"choice\\\":\\\"inspect_assertion\\\"}\"}}]}")]
    [InlineData(AdviceProtocol.Ollama, "{\"done\":true,\"done_reason\":\"stop\",\"message\":{\"content\":\"{\\\"choice\\\":\\\"inspect_assertion\\\"}\"}}")]
    [InlineData(AdviceProtocol.Classifier, "{\"label\":\"inspect_assertion\"}")]
    [InlineData(AdviceProtocol.Custom, "{\"schema\":\"assay-advice-response/0.1\",\"status\":\"suggested\",\"choice\":\"inspect_assertion\"}")]
    public void Every_protocol_maps_to_the_same_bounded_action(AdviceProtocol protocol, string json)
    {
        var evidence = Evidence();
        var advice = ProviderAdvice.Parse(protocol, evidence, Encoding.UTF8.GetBytes(json));
        Assert.Equal(NextAction.InspectAssertion, Assert.IsType<Recommendation>(advice.Value).Action);
        Assert.Equal(VerificationVerdict.Fail, evidence.VerificationVerdict);
    }

    [Theory]
    [InlineData(AdviceProtocol.Decisions, "{\"answers\":[{\"name\":\"other\",\"type\":\"choice\",\"choice\":\"inspect_assertion\"}]}")]
    [InlineData(AdviceProtocol.Cloudflare, "{\"success\":false,\"result\":{}}")]
    [InlineData(AdviceProtocol.Chat, "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"{\\\"choice\\\":\\\"inspect_assertion\\\"}\"}}]}")]
    [InlineData(AdviceProtocol.Ollama, "{\"done\":false,\"message\":{\"content\":\"{\\\"choice\\\":\\\"inspect_assertion\\\"}\"}}")]
    [InlineData(AdviceProtocol.Classifier, "{\"label\":3}")]
    [InlineData(AdviceProtocol.Custom, "{\"schema\":\"wrong\",\"status\":\"suggested\",\"choice\":\"inspect_assertion\"}")]
    public void Misbound_incomplete_and_invalid_responses_are_unavailable(AdviceProtocol protocol, string json)
    {
        Assert.IsType<AdviceUnavailable>(ProviderAdvice.Parse(protocol, Evidence(), Encoding.UTF8.GetBytes(json)).Value);
    }

    [Theory]
    [InlineData(AdviceProtocol.Decisions, "{\"answers\":[{\"name\":\"next\",\"type\":\"refusal\"}]}")]
    [InlineData(AdviceProtocol.Classifier, "{\"label\":\"Pass\"}")]
    [InlineData(AdviceProtocol.Custom, "{\"schema\":\"assay-advice-response/0.1\",\"status\":\"review\"}")]
    [InlineData(AdviceProtocol.Chat, "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"refusal\":\"declined\"}}]}")]
    public void Refusal_and_unknown_labels_require_review(AdviceProtocol protocol, string json)
    {
        Assert.IsType<ReviewRequired>(ProviderAdvice.Parse(protocol, Evidence(), Encoding.UTF8.GetBytes(json)).Value);
    }

    [Fact]
    public void Openai_and_systemone_payloads_preserve_the_same_question_without_exposing_a_verdict()
    {
        using var openai = JsonDocument.Parse(JsonSerializer.Serialize(ProviderAdvice.Payload(Settings("openai"), Evidence())));
        using var laya = JsonDocument.Parse(JsonSerializer.Serialize(ProviderAdvice.Payload(Settings("laya"), Evidence())));
        Assert.Equal("next", openai.RootElement.GetProperty("questions")[0].GetProperty("name").GetString());
        Assert.Equal(4, openai.RootElement.GetProperty("questions")[0].GetProperty("choices").GetArrayLength());
        Assert.Equal(4, laya.RootElement.GetProperty("questions").GetProperty("next").GetProperty("criteria").EnumerateObject().Count());
        Assert.DoesNotContain("verificationVerdict", openai.RootElement.GetProperty("input").GetString() ?? "", StringComparison.Ordinal);
        Assert.False(laya.RootElement.GetProperty("state").TryGetProperty("verificationVerdict", out _));
    }

    [Theory]
    [InlineData("http://example.com/predict", "model", "")]
    [InlineData("https://user:password@example.com/predict", "model", "")]
    [InlineData("https://example.com/predict?token=secret", "model", "")]
    [InlineData("http://127.0.0.1:8001/predict", "", "")]
    [InlineData("http://127.0.0.1:8001/predict", "model", "bad\nkey")]
    public async Task Invalid_configuration_sends_no_request(string endpoint, string model, string key)
    {
        var attempts = 0;
        using var handler = new Handler(_ => { attempts++; return new HttpResponseMessage(HttpStatusCode.OK); });
        using var client = new HttpClient(handler);
        var settings = new ProviderSettings("custom", AdviceProtocol.Custom, endpoint, model, "KEY", false);
        Assert.IsType<AdviceUnavailable>((await ProviderAdvice.RequestAsync(client, settings, Evidence(), key, CancellationToken.None)).Value);
        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task Rate_limit_is_one_attempt_with_no_provider_shopping()
    {
        var attempts = 0;
        using var handler = new Handler(_ => { attempts++; return new HttpResponseMessage(HttpStatusCode.TooManyRequests); });
        using var client = new HttpClient(handler);
        var result = await ProviderAdvice.RequestAsync(client, Settings("openai"), Evidence(), "synthetic", CancellationToken.None);
        Assert.IsType<AdviceUnavailable>(result.Value);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Ineligible_experiment_never_calls_a_provider()
    {
        var attempts = 0;
        using var handler = new Handler(_ => { attempts++; return new HttpResponseMessage(HttpStatusCode.OK); });
        using var client = new HttpClient(handler);
        var evidence = Evidence() with { State = new IncompleteTestRun() };
        Assert.IsType<ReviewRequired>((await ProviderAdvice.RequestAsync(client, Settings("qwen"), evidence, "", CancellationToken.None)).Value);
        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task Local_services_receive_a_bounded_content_length_instead_of_chunked_input()
    {
        using var handler = new Handler(request =>
        {
            var content = Assert.IsAssignableFrom<HttpContent>(request.Content);
            Assert.True(content.Headers.ContentLength > 0);
            Assert.Equal("application/json", content.Headers.ContentType?.MediaType);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"label\":\"inspect_assertion\"}")
            };
        });
        using var client = new HttpClient(handler);
        var result = await ProviderAdvice.RequestAsync(client, Settings("setfit", key => key == "ASSAY_SETFIT_MODEL" ? "local-model" : string.Empty), Evidence(), "", CancellationToken.None);
        Assert.IsType<Recommendation>(result.Value);
    }

    [Theory]
    [InlineData(Observation.Unknown, Observation.Unknown)]
    [InlineData(Observation.Present, Observation.Unknown)]
    [InlineData(Observation.Unknown, Observation.Present)]
    public async Task Missing_context_is_reviewed_before_any_provider_request(Observation covered, Observation assertion)
    {
        var attempts = 0;
        using var handler = new Handler(_ => { attempts++; return new HttpResponseMessage(HttpStatusCode.OK); });
        using var client = new HttpClient(handler);
        var evidence = Evidence() with { Covered = covered, HasBoundaryAssertion = assertion };
        var result = await ProviderAdvice.RequestAsync(client, Settings("openai"), evidence, "synthetic", CancellationToken.None);
        Assert.IsType<ReviewRequired>(result.Value);
        Assert.False(Assistance.Eligible(evidence));
        Assert.Equal(0, attempts);
        Assert.Equal(VerificationVerdict.Fail, evidence.VerificationVerdict);
    }

    [Fact]
    public void Cloudflare_account_and_provider_names_are_validated()
    {
        Assert.IsType<ProviderConfigurationError>(ProviderAdvice.Configure("clef", _ => "invalid").Value);
        Assert.IsType<ProviderConfigurationError>(ProviderAdvice.Configure("unknown", _ => string.Empty).Value);
        var settings = Settings("clef-flash", _ => new string('a', 32));
        Assert.EndsWith("/ai/run/@cf/cloudflare/clef-flash", settings.Endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Setfit_contract_uses_text_and_requires_registered_labels_instead_of_dynamic_questions()
    {
        var settings = Settings("setfit", _ => "reviewed-local-model");
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ProviderAdvice.Payload(settings, Evidence())));
        Assert.True(payload.RootElement.TryGetProperty("text", out _));
        Assert.False(payload.RootElement.TryGetProperty("questions", out _));
        Assert.IsType<ReviewRequired>(ProviderAdvice.Parse(AdviceProtocol.Classifier, Evidence(), Encoding.UTF8.GetBytes("{\"label\":\"human_review\"}")).Value);
    }

    private static ProviderSettings Settings(string name) => Settings(name, _ => string.Empty);
    private static ProviderSettings Settings(string name, Func<string, string> environment) => Assert.IsType<ProviderSettings>(ProviderAdvice.Configure(name, environment).Value);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
