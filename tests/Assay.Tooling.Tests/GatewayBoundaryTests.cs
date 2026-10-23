namespace Assay.Tooling.Tests;

public sealed class GatewayBoundaryTests
{
    internal static JsonNode Question()
    {
        return Json.Parse(/*lang=json,strict*/ """{"type":"choice","instructions":"Choose an action.","criteria":{"inspect_assertion":"Inspect assertion","human_review":"Obtain evidence"}}""");
    }

    private static readonly string[] expected = new[] { "inspect_assertion", "human_review" };

    [Fact]
    public void MapsChoiceWithoutInventingConfidence()
    {
        var question = Question(); var payload = LocalServices.OllamaPayload("small-model", Json.Create(("synthetic", true)), question); Assert.False(payload.B("think")); Assert.Equal(expected, payload["format"]!["properties"]!["choice"].Strings("enum")); var result = LocalServices.OllamaResponse(/*lang=json,strict*/ """{"done":true,"done_reason":"stop","message":{"content":"{\"choice\":\"inspect_assertion\"}"}}""", question); Assert.Equal("choice", result.S("type")); Assert.Null(result["confidence"]);
    }
    [Fact]
    public void UnknownActionRefuses()
    {
        Assert.Equal("refusal", LocalServices.OllamaResponse(/*lang=json,strict*/ """{"done":true,"done_reason":"stop","message":{"content":"{\"choice\":\"Pass\"}"}}""", Question()).S("type"));
    }

    [Fact]
    public void IncompleteGenerationRejected()
    {
        _ = Assert.Throws<InvalidDataException>(() => LocalServices.OllamaResponse(/*lang=json,strict*/ "{\"done\":false}", Question()));
    }

    [Fact]
    public void OversizedOutputRejected()
    {
        _ = Assert.Throws<InvalidDataException>(() => LocalServices.OllamaResponse(new string(' ', 65537), Question()));
    }

    [Fact] public void ScoreCannotMasqueradeAsChoice() { var q = Question(); q["type"] = "score"; _ = Assert.Throws<InvalidDataException>(() => LocalServices.OllamaPayload("small", Json.Create(), q)); }
    [Fact]
    public void ConfiguredKeysCannotAuthorizeLiveCalls()
    {
        _ = Assert.Throws<InvalidDataException>(() => AdviceEvaluation.ValidateRequest(false, ["jev"], 8, 0));
    }

    [Fact]
    public void MissingKeyStopsBeforeAnyProviderCall()
    {
        Assert.Throws<InvalidDataException>(() => AdviceEvaluation.ValidateConfiguration(["openai"], _ => false));
    }
    [Fact]
    public void BudgetCheckedBeforeProviderInference()
    {
        _ = Assert.Throws<InvalidDataException>(() => AdviceEvaluation.ValidateRequest(true, ["jev"], 1, 2));
    }
}
