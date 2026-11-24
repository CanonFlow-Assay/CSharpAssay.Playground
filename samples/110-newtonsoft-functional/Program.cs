using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NewtonsoftFunctional;

public static class Program
{
    public static int Main(string[] args) => args switch
    {
        ["--verify"] => Verify(),
        ["--benchmark"] => Benchmark(),
        _ => Usage()
    };

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: NewtonsoftFunctional --verify | --benchmark");
        return 64;
    }

    private static int Verify()
{
    var cases = new (string Name, string Json, string ExpectedPrefix)[]
    {
        ("valid", "{\"key\":\"checkout-v2\",\"enabled\":true,\"rolloutPercent\":25,\"audiences\":[\"staff\",\"beta\"]}", "accepted|checkout-v2|True|25|staff,beta"),
        ("different-order", "{\"audiences\":[\"staff\"],\"rolloutPercent\":0,\"enabled\":false,\"key\":\"checkout-v2\"}", "accepted|checkout-v2|False|0|staff"),
        ("missing-key", "{\"enabled\":true,\"rolloutPercent\":25,\"audiences\":[]}", "rejected|missing-property:key"),
        ("duplicate-key", "{\"key\":\"first\",\"key\":\"second\",\"enabled\":true,\"rolloutPercent\":25,\"audiences\":[]}", "rejected|duplicate-property:A duplicate property was observed."),
        ("unknown-property", "{\"key\":\"checkout-v2\",\"enabled\":true,\"rolloutPercent\":25,\"audiences\":[],\"expires\":\"tomorrow\"}", "rejected|unknown-property:expires"),
        ("fractional-rollout", "{\"key\":\"checkout-v2\",\"enabled\":true,\"rolloutPercent\":25.5,\"audiences\":[]}", "rejected|invalid-property:rolloutPercent:number"),
        ("non-string-audience", "{\"key\":\"checkout-v2\",\"enabled\":true,\"rolloutPercent\":25,\"audiences\":[\"staff\",42]}", "rejected|invalid-property:audiences:array-has-non-text"),
        ("invalid-json", "{\"key\":", "rejected|invalid-json:Input could not be parsed as JSON.")
    };

    foreach (var (name, json, expectedPrefix) in cases)
    {
        var newtonsoft = FeatureFlagPolicy.Canonical(NewtonsoftProjection.Parse(json));
        var functional = FeatureFlagPolicy.Canonical(FunctionalProjection.Parse(json));
        if (!string.Equals(newtonsoft, functional, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"{name}: adapters disagree. Newtonsoft={newtonsoft} Functional={functional}");
            return 1;
        }
        if (!newtonsoft.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"{name}: expected prefix '{expectedPrefix}', got '{newtonsoft}'.");
            return 1;
        }
    }
    Console.WriteLine($"Newtonsoft functional contract: {cases.Length} cases passed.");
    return 0;
}

    private static int Benchmark()
{
    var samples = new List<object>();
    foreach (var count in new[] { 10_000, 100_000 })
    {
        var documents = CreateDocuments(count);
        var expectedDigest = Digest(documents, FunctionalProjection.Parse);
        for (var iteration = -2; iteration < 5; iteration++)
        {
            foreach (var mode in iteration % 2 == 0 ? new[] { "newtonsoft", "functional" } : new[] { "functional", "newtonsoft" })
            {
                Func<string, FeatureProjection> projection = mode == "newtonsoft" ? NewtonsoftProjection.Parse : FunctionalProjection.Parse;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var before = GC.GetTotalAllocatedBytes(precise: true);
                var start = Stopwatch.GetTimestamp();
                var digest = Digest(documents, projection);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
                if (!string.Equals(expectedDigest, digest, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"{mode} output differs from the preflight functional projection; timing is invalid.");
                    return 2;
                }
                samples.Add(new { mode, iteration, warmup = iteration < 0, documents = count, elapsedMilliseconds = elapsed, allocatedBytes = allocated, digest });
            }
        }
    }
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        schema = "newtonsoft-functional-boundary-benchmark/0.1",
        executionStatus = "completed",
        samples,
        runtime = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        serverGc = System.Runtime.GCSettings.IsServerGC,
        tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
        processorCount = Environment.ProcessorCount,
        stopwatchFrequency = Stopwatch.Frequency,
        allocationCounter = "Process-wide managed allocation delta; includes background allocation and is not retained or peak memory.",
        scope = "Strict feature-flag JSON parsing plus equivalent raw-to-domain projection and an identical SHA-256 output sink. This is not a general Newtonsoft.Json versus System.Text.Json benchmark."
    }));
    return 0;
}

    private static List<string> CreateDocuments(int count)
{
    var documents = new List<string>(count);
    for (var index = 0; index < count; index++)
    {
        documents.Add($"{{\"key\":\"checkout-{index % 32}\",\"enabled\":{(index % 2 == 0 ? "true" : "false")},\"rolloutPercent\":{index % 101},\"audiences\":[\"staff\",\"beta\"]}}");
    }
    return documents;
}

    private static string Digest(IEnumerable<string> documents, Func<string, FeatureProjection> projection)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var document in documents)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(FeatureFlagPolicy.Canonical(projection(document))));
        hash.AppendData([0]);
    }
    return Convert.ToHexString(hash.GetHashAndReset());
}
}

public sealed record JsonText(string Value);
public sealed record JsonBoolean(bool Value);
public sealed record JsonWholeNumber(int Value);
public sealed record JsonTextArray(ImmutableArray<string> Values);
public sealed record JsonMissing;
public sealed record JsonInvalid(string ActualKind);
public readonly union JsonField(JsonText, JsonBoolean, JsonWholeNumber, JsonTextArray, JsonMissing, JsonInvalid);
public sealed record RawFeatureFlag(ImmutableDictionary<string, JsonField> Fields, ImmutableArray<ProjectionIssue> ParseIssues);
public sealed record FeatureFlag(string Key, bool Enabled, int RolloutPercent, ImmutableArray<string> Audiences);
public sealed record ProjectionIssue(string Code, string Detail);
public sealed record ProjectionAccepted(FeatureFlag Value);
public sealed record ProjectionRejected(ImmutableArray<ProjectionIssue> Issues);
public readonly union FeatureProjection(ProjectionAccepted, ProjectionRejected);

public static class FeatureFlagPolicy
{
    private static readonly ImmutableHashSet<string> RequiredNames = ImmutableHashSet.Create(StringComparer.Ordinal, "key", "enabled", "rolloutPercent", "audiences");

    public static FeatureProjection Decide(RawFeatureFlag input)
    {
        var issues = input.ParseIssues.ToBuilder();
        var key = Read(input.Fields, "key");
        var enabled = Read(input.Fields, "enabled");
        var rollout = Read(input.Fields, "rolloutPercent");
        var audiences = Read(input.Fields, "audiences");
        ValidateText(key, "key", issues);
        ValidateBoolean(enabled, "enabled", issues);
        ValidateWhole(rollout, "rolloutPercent", issues);
        ValidateAudience(audiences, "audiences", issues);
        foreach (var name in input.Fields.Keys.Where(name => !RequiredNames.Contains(name)))
        {
            issues.Add(new ProjectionIssue("unknown-property", name));
        }
        if (rollout is JsonWholeNumber number && (number.Value < 0 || number.Value > 100))
        {
            issues.Add(new ProjectionIssue("rollout-out-of-range", "rolloutPercent must be between 0 and 100."));
        }
        return issues.Count == 0 && key is JsonText keyValue && enabled is JsonBoolean enabledValue && rollout is JsonWholeNumber rolloutValue && audiences is JsonTextArray audienceValue
            ? new ProjectionAccepted(new FeatureFlag(keyValue.Value, enabledValue.Value, rolloutValue.Value, audienceValue.Values))
            : new ProjectionRejected(issues.ToImmutable());
    }

    public static string Canonical(FeatureProjection projection) => projection switch
    {
        ProjectionAccepted accepted => $"accepted|{accepted.Value.Key}|{accepted.Value.Enabled}|{accepted.Value.RolloutPercent}|{string.Join(',', accepted.Value.Audiences)}",
        ProjectionRejected rejected => $"rejected|{string.Join(';', rejected.Issues.Select(issue => $"{issue.Code}:{issue.Detail}"))}"
    };

    // Deliberately exhaustive: a new JsonField member must be classified by every validator.
    private static void ValidateText(JsonField field, string name, ImmutableArray<ProjectionIssue>.Builder issues)
    {
        switch (field)
        {
            case JsonText text when !string.IsNullOrWhiteSpace(text.Value): return;
            case JsonText: issues.Add(new ProjectionIssue("blank-property", name)); return;
            case JsonMissing: issues.Add(new ProjectionIssue("missing-property", name)); return;
            case JsonInvalid invalid: issues.Add(new ProjectionIssue("invalid-property", $"{name}:{invalid.ActualKind}")); return;
            case JsonBoolean: issues.Add(new ProjectionIssue("invalid-property", name)); return;
            case JsonWholeNumber: issues.Add(new ProjectionIssue("invalid-property", name)); return;
            case JsonTextArray: issues.Add(new ProjectionIssue("invalid-property", name)); return;
        }
    }

    private static void ValidateBoolean(JsonField field, string name, ImmutableArray<ProjectionIssue>.Builder issues)
    {
        switch (field)
        {
            case JsonBoolean: return;
            case JsonMissing: issues.Add(new ProjectionIssue("missing-property", name)); return;
            case JsonInvalid invalid: issues.Add(new ProjectionIssue("invalid-property", $"{name}:{invalid.ActualKind}")); return;
            case JsonText: issues.Add(new ProjectionIssue("invalid-property", name)); return;
            case JsonWholeNumber: issues.Add(new ProjectionIssue("invalid-property", name)); return;
            case JsonTextArray: issues.Add(new ProjectionIssue("invalid-property", name)); return;
        }
    }

    private static void ValidateWhole(JsonField field, string name, ImmutableArray<ProjectionIssue>.Builder issues)
    {
        switch (field)
        {
            case JsonWholeNumber: return;
            case JsonMissing: issues.Add(new ProjectionIssue("missing-property", name)); return;
            case JsonInvalid invalid: issues.Add(new ProjectionIssue("invalid-property", $"{name}:{invalid.ActualKind}")); return;
            case JsonText: issues.Add(new ProjectionIssue("invalid-property", name)); return;
            case JsonBoolean: issues.Add(new ProjectionIssue("invalid-property", name)); return;
            case JsonTextArray: issues.Add(new ProjectionIssue("invalid-property", name)); return;
        }
    }

    private static void ValidateAudience(JsonField field, string name, ImmutableArray<ProjectionIssue>.Builder issues)
    {
        switch (field)
        {
            case JsonTextArray: return;
            case JsonMissing: issues.Add(new ProjectionIssue("missing-property", name)); return;
            case JsonInvalid invalid: issues.Add(new ProjectionIssue("invalid-property", $"{name}:{invalid.ActualKind}")); return;
            case JsonText: issues.Add(new ProjectionIssue("invalid-property", name)); return;
            case JsonBoolean: issues.Add(new ProjectionIssue("invalid-property", name)); return;
            case JsonWholeNumber: issues.Add(new ProjectionIssue("invalid-property", name)); return;
        }
    }

    private static JsonField Read(ImmutableDictionary<string, JsonField> fields, string name) => fields.TryGetValue(name, out var value) ? value : new JsonMissing();
}

public static class NewtonsoftProjection
{
    public static FeatureProjection Parse(string json)
    {
        try
        {
            using var text = new StringReader(json);
            using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
            var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Ignore, LineInfoHandling = LineInfoHandling.Ignore });
            return token is JObject value ? FeatureFlagPolicy.Decide(FromObject(value)) : FeatureFlagPolicy.Decide(InvalidRoot(TokenKind(token)));
        }
        catch (JsonReaderException exception) when (exception.Message.Contains("already exists", StringComparison.Ordinal))
        {
            return new ProjectionRejected([new ProjectionIssue("duplicate-property", "A duplicate property was observed.")]);
        }
        catch (JsonReaderException)
        {
            return FeatureFlagPolicy.Decide(InvalidJson());
        }
    }

    private static RawFeatureFlag FromObject(JObject value)
    {
        var fields = ImmutableDictionary.CreateBuilder<string, JsonField>(StringComparer.Ordinal);
        foreach (var property in value.Properties()) { fields[property.Name] = FromToken(property.Value); }
        return new RawFeatureFlag(fields.ToImmutable(), []);
    }

    private static JsonField FromToken(JToken token) => token switch
    {
        JValue { Type: JTokenType.String, Value: string value } => new JsonText(value),
        JValue { Type: JTokenType.Boolean, Value: bool value } => new JsonBoolean(value),
        JValue { Type: JTokenType.Integer, Value: long value } when value is >= int.MinValue and <= int.MaxValue => new JsonWholeNumber((int)value),
        JArray array => ReadTextArray(array),
        _ => new JsonInvalid(TokenKind(token))
    };

    private static JsonField ReadTextArray(JArray array)
    {
        var values = ImmutableArray.CreateBuilder<string>();
        foreach (var item in array)
        {
            if (item is not JValue { Type: JTokenType.String, Value: string value }) { return new JsonInvalid("array-has-non-text"); }
            values.Add(value);
        }
        return new JsonTextArray(values.ToImmutable());
    }

    private static string TokenKind(JToken token) => token.Type switch
    {
        JTokenType.Integer or JTokenType.Float => "number",
        JTokenType.Array => "array-has-non-text",
        JTokenType.String => "string",
        JTokenType.Boolean => "boolean",
        JTokenType.Null => "null",
        _ => "other"
    };

    private static RawFeatureFlag InvalidJson() => new(ImmutableDictionary<string, JsonField>.Empty, [new ProjectionIssue("invalid-json", "Input could not be parsed as JSON.")]);
    private static RawFeatureFlag InvalidRoot(string actualKind) => new(ImmutableDictionary<string, JsonField>.Empty, [new ProjectionIssue("root-not-object", actualKind)]);
}

public static class FunctionalProjection
{
    public static FeatureProjection Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            return document.RootElement.ValueKind == JsonValueKind.Object ? FeatureFlagPolicy.Decide(FromObject(document.RootElement)) : FeatureFlagPolicy.Decide(InvalidRoot(ElementKind(document.RootElement)));
        }
        catch (System.Text.Json.JsonException)
        {
            return FeatureFlagPolicy.Decide(InvalidJson());
        }
    }

    private static RawFeatureFlag FromObject(JsonElement value)
    {
        var fields = ImmutableDictionary.CreateBuilder<string, JsonField>(StringComparer.Ordinal);
        var issues = ImmutableArray.CreateBuilder<ProjectionIssue>();
        foreach (var property in value.EnumerateObject())
        {
            if (!fields.TryAdd(property.Name, FromElement(property.Value))) { issues.Add(new ProjectionIssue("duplicate-property", "A duplicate property was observed.")); }
        }
        return new RawFeatureFlag(fields.ToImmutable(), issues.ToImmutable());
    }

    private static JsonField FromElement(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => new JsonText(value.GetString() ?? string.Empty),
        JsonValueKind.True => new JsonBoolean(true),
        JsonValueKind.False => new JsonBoolean(false),
        JsonValueKind.Number when value.TryGetInt32(out var number) => new JsonWholeNumber(number),
        JsonValueKind.Array => ReadTextArray(value),
        _ => new JsonInvalid(ElementKind(value))
    };

    private static JsonField ReadTextArray(JsonElement value)
    {
        var values = ImmutableArray.CreateBuilder<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) { return new JsonInvalid("array-has-non-text"); }
            values.Add(item.GetString() ?? string.Empty);
        }
        return new JsonTextArray(values.ToImmutable());
    }

    private static string ElementKind(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => "number",
        JsonValueKind.Array => "array-has-non-text",
        JsonValueKind.String => "string",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => "other"
    };

    private static RawFeatureFlag InvalidJson() => new(ImmutableDictionary<string, JsonField>.Empty, [new ProjectionIssue("invalid-json", "Input could not be parsed as JSON.")]);
    private static RawFeatureFlag InvalidRoot(string actualKind) => new(ImmutableDictionary<string, JsonField>.Empty, [new ProjectionIssue("root-not-object", actualKind)]);
}
