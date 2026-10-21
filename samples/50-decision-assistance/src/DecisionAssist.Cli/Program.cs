using System.Text.Json;
using Playground.DecisionAssist.Cli;
using Playground.DecisionAssist.Domain;

if (args is ["--list-providers"])
{
    Console.WriteLine(JsonSerializer.Serialize(new { providers = ProviderAdvice.Names, defaultProvider = "none", automaticFallback = false }));
    return 0;
}
if (args.Length < 1 || args.Length > 3 ||
    args.Skip(1).Any(option => option is not ("--jev" or "--firm") && !option.StartsWith("--provider=", StringComparison.Ordinal)) ||
    args.Skip(1).Distinct(StringComparer.Ordinal).Count() != args.Length - 1 ||
    args.Skip(1).Count(option => option == "--jev" || option.StartsWith("--provider=", StringComparison.Ordinal)) > 1)
{
    Console.Error.WriteLine("Usage: decision-assist <experiment.json> [--provider=<name>|--jev] [--firm]; or --list-providers");
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
        VerificationVerdict.Inconclusive)
    {
        Task = ReadText(root, "task", "synthetic-zero-boundary-mutation", 128),
        InvestigationContext = ReadText(root, "investigationContext", string.Empty, 1024)
    };
    Advice baseline = Assistance.Baseline(evidence);
    Advice advice = baseline;
    var provider = "deterministic-baseline";
    var requestedModel = "none";
    var selected = args.Skip(1).FirstOrDefault(option => option.StartsWith("--provider=", StringComparison.Ordinal));
    var name = selected is null ? (args.Contains("--jev", StringComparer.Ordinal) ? "jev" : "none") : selected[11..];
    if (name != "none")
    {
        var configuration = ProviderAdvice.Configure(name, key => Environment.GetEnvironmentVariable(key) ?? string.Empty);
        if (configuration is not ProviderSettings settings)
        {
            Console.Error.WriteLine("Invalid provider configuration; see the sample README.");
            return 64;
        }
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        advice = await ProviderAdvice.RequestAsync(client, settings, evidence, Environment.GetEnvironmentVariable(settings.KeyVariable) ?? string.Empty, deadline.Token);
        provider = name + "-optional-unqualified";
        requestedModel = settings.Model;
    }
    var tone = args.Contains("--firm", StringComparer.Ordinal) ? FeedbackTone.Firm : FeedbackTone.Coaching;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schema = "playground-decision-advice/0.1",
        sourceSha256 = evidence.SourceSha256,
        task = evidence.Task,
        investigationContext = evidence.InvestigationContext,
        provider,
        requestedModel,
        modelRevision = "NotObserved",
        providerRequestEligible = name != "none" && Assistance.Eligible(evidence),
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
catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine("Invalid experiment or provider configuration; use --list-providers and the sample README.");
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

static string ReadText(JsonElement root, string name, string fallback, int maximumLength)
{
    var text = root.TryGetProperty(name, out var value) ? value.GetString() ?? throw new JsonException() : fallback;
    return text.Length <= maximumLength && !text.Any(char.IsControl)
        ? text : throw new JsonException("Invalid bounded sample metadata.");
}
