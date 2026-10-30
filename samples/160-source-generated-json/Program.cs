using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SourceGeneratedJson;

public static class Program
{
    public static int Main(string[] args) => args switch
    {
        ["--verify"] => Verify(),
        _ => Usage()
    };

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: SourceGeneratedJson --verify");
        return 64;
    }

    private static int Verify()
    {
        var cases = new (string Name, string Json, string Expected)[]
        {
            ("valid", "{\"key\":\"checkout-v2\",\"enabled\":true,\"rolloutPercent\":25,\"audiences\":[\"staff\",\"beta\"]}", "accepted|checkout-v2|True|25|staff,beta"),
            ("duplicate", "{\"key\":\"first\",\"key\":\"second\",\"enabled\":true,\"rolloutPercent\":25,\"audiences\":[]}", "rejected|duplicate-property:key"),
            ("unknown", "{\"key\":\"checkout-v2\",\"enabled\":true,\"rolloutPercent\":25,\"audiences\":[],\"expires\":\"tomorrow\"}", "rejected|unknown-property:expires"),
            ("bad-type", "{\"key\":\"checkout-v2\",\"enabled\":true,\"rolloutPercent\":25.5,\"audiences\":[]}", "rejected|invalid-property:rolloutPercent"),
            ("range", "{\"key\":\"checkout-v2\",\"enabled\":true,\"rolloutPercent\":101,\"audiences\":[]}", "rejected|rollout-out-of-range:rolloutPercent"),
            ("invalid", "{\"key\":", "rejected|invalid-json:document")
        };

        foreach (var test in cases)
        {
            var actual = ContractPolicy.Canonical(SourceGeneratedContract.Parse(test.Json));
            if (!string.Equals(actual, test.Expected, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"{test.Name}: expected {test.Expected}, got {actual}.");
                return 1;
            }
        }

        Console.WriteLine($"Source-generated JSON contract: {cases.Length} cases passed.");
        return 0;
    }
}

public sealed record FeatureEnvelope(string Key, bool Enabled, int RolloutPercent, ImmutableArray<string> Audiences);
public sealed class FeatureEnvelopeWire
{
    public string Key { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public int RolloutPercent { get; init; }
    public string[] Audiences { get; init; } = [];
}
public sealed record ContractAccepted(FeatureEnvelope Value);
public sealed record ContractRejected(ImmutableArray<ContractIssue> Issues);
public readonly union ContractResult(ContractAccepted, ContractRejected);
public sealed record ContractIssue(string Code, string Detail);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FeatureEnvelopeWire))]
public partial class FeatureEnvelopeJsonContext : JsonSerializerContext;

public static class SourceGeneratedContract
{
    private static readonly ImmutableHashSet<string> Names = ImmutableHashSet.Create(StringComparer.Ordinal, "key", "enabled", "rolloutPercent", "audiences");

    public static ContractResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            var gate = Validate(document.RootElement);
            if (gate.Length != 0) { return new ContractRejected(gate); }
            var wire = JsonSerializer.Deserialize(json, FeatureEnvelopeJsonContext.Default.FeatureEnvelopeWire);
            return wire is FeatureEnvelopeWire value
                ? ContractPolicy.Decide(new FeatureEnvelope(value.Key, value.Enabled, value.RolloutPercent, value.Audiences.ToImmutableArray()))
                : new ContractRejected([new ContractIssue("deserialization-failed", "feature-envelope")]);
        }
        catch (JsonException)
        {
            return new ContractRejected([new ContractIssue("invalid-json", "document")]);
        }
    }

    private static ImmutableArray<ContractIssue> Validate(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) { return [new ContractIssue("root-not-object", root.ValueKind.ToString())]; }
        var issues = ImmutableArray.CreateBuilder<ContractIssue>();
        var seen = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name)) { issues.Add(new ContractIssue("duplicate-property", property.Name)); continue; }
            if (!Names.Contains(property.Name)) { issues.Add(new ContractIssue("unknown-property", property.Name)); continue; }
            if (!IsExpectedKind(property.Name, property.Value)) { issues.Add(new ContractIssue("invalid-property", property.Name)); }
        }
        foreach (var name in Names.Where(name => !seen.Contains(name)).OrderBy(name => name, StringComparer.Ordinal)) { issues.Add(new ContractIssue("missing-property", name)); }
        return issues.ToImmutable();
    }

    private static bool IsExpectedKind(string name, JsonElement value) => name switch
    {
        "key" => value.ValueKind == JsonValueKind.String && value.GetString() is string key && !string.IsNullOrWhiteSpace(key),
        "enabled" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "rolloutPercent" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
        "audiences" => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String),
        _ => false
    };
}

public static class ContractPolicy
{
    public static ContractResult Decide(FeatureEnvelope envelope) => envelope.RolloutPercent is >= 0 and <= 100
        ? new ContractAccepted(envelope)
        : new ContractRejected([new ContractIssue("rollout-out-of-range", "rolloutPercent")]);

    public static string Canonical(ContractResult result) => result switch
    {
        ContractAccepted accepted => $"accepted|{accepted.Value.Key}|{accepted.Value.Enabled}|{accepted.Value.RolloutPercent}|{string.Join(',', accepted.Value.Audiences)}",
        ContractRejected rejected => $"rejected|{string.Join(';', rejected.Issues.Select(issue => $"{issue.Code}:{issue.Detail}"))}"
    };
}
