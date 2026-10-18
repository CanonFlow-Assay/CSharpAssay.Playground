using System.Text.Json;
using Playground.DecisionAssist.Cli;
using Playground.DecisionAssist.Domain;

if (args.Length < 1 || args.Length > 3 ||
    args.Skip(1).Any(option => option is not ("--jev" or "--firm")) ||
    args.Skip(1).Distinct(StringComparer.Ordinal).Count() != args.Length - 1)
{
    Console.Error.WriteLine("Usage: decision-assist <experiment.json> [--jev] [--firm]");
    return 64;
}

try
{
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
    var root = document.RootElement;
    if (root.GetProperty("schema").GetString() != "playground-mutation-experiment/0.1" ||
        !root.GetProperty("synthetic").GetBoolean())
    {
        throw new JsonException("Only this sample's synthetic experiment schema is supported.");
    }
    var sourceHash = root.GetProperty("sourceSha256").GetString() ?? throw new JsonException();
    if (sourceHash.Length != 64 || !sourceHash.All(char.IsAsciiHexDigit))
    {
        throw new JsonException("Expected a SHA-256 identity.");
    }
    var evidence = new MutationEvidence(
        sourceHash,
        Assistance.Observe(root.GetProperty("baselinePassed").GetBoolean(),
        root.GetProperty("mutantCompiled").GetBoolean(),
        root.GetProperty("testsCompleted").GetBoolean(),
        root.GetProperty("survived").GetBoolean()),
        ReadObservation(root.GetProperty("covered")),
        ReadObservation(root.GetProperty("hasBoundaryAssertion")),
        VerificationVerdict.Inconclusive);
    Advice baseline = Assistance.Baseline(evidence);
    Advice advice = baseline;
    var provider = "deterministic-baseline";
    if (args.Contains("--jev", StringComparer.Ordinal))
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        advice = await JevAdvice.RequestAsync(client, evidence, Environment.GetEnvironmentVariable("TYPESAFE_API_KEY") ?? string.Empty, deadline.Token);
        provider = "jev-optional-unqualified";
    }
    var tone = args.Contains("--firm", StringComparer.Ordinal) ? FeedbackTone.Firm : FeedbackTone.Coaching;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schema = "playground-decision-advice/0.1",
        sourceSha256 = evidence.SourceSha256,
        provider,
        requestedModel = provider == "jev-optional-unqualified" ? "jev-latest" : "none",
        modelRevision = "NotObserved",
        deterministicAction = baseline switch { Recommendation suggestion => suggestion.Action.ToString(), ReviewRequired => "HumanReview", AdviceUnavailable => "None" },
        deterministicMessage = Assistance.Explain(baseline, tone),
        adviceStatus = advice switch { Recommendation => "suggested", ReviewRequired => "review", AdviceUnavailable => "unavailable" },
        action = advice switch { Recommendation suggestion => suggestion.Action.ToString(), ReviewRequired => "HumanReview", AdviceUnavailable => "None" },
        message = Assistance.Explain(advice, tone),
        verificationVerdict = evidence.VerificationVerdict.ToString(),
        authority = "Advisory only; this experiment is not an Assay verification receipt."
    }));
    return 0;
}
catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
{
    Console.Error.WriteLine("Cannot read a valid synthetic experiment receipt.");
    return 2;
}

static Observation ReadObservation(JsonElement value)
{
    return value.ValueKind switch
    {
        JsonValueKind.True => Observation.Present,
        JsonValueKind.False => Observation.Absent,
        JsonValueKind.Null => Observation.Unknown,
        _ => throw new JsonException("Expected a boolean observation or null at the input boundary.")
    };
}
