using System.Collections.Immutable;

namespace ConfigurationFirewall;

public static class Program
{
    public static int Main(string[] args) => args switch
    {
        ["--verify"] => Verify(),
        _ => Usage()
    };

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: ConfigurationFirewall --verify");
        return 64;
    }

    private static int Verify()
    {
        var cases = new (string Name, ImmutableDictionary<string, string> Values, string Expected)[]
        {
            ("valid", Values(("Service:Endpoint", "https://api.example.test"), ("Service:RetryLimit", "3"), ("Service:Mode", "read-only")), "loaded|https://api.example.test/|3|ReadOnly"),
            ("missing", Values(("Service:Endpoint", "https://api.example.test"), ("Service:RetryLimit", "3")), "rejected|missing:Service:Mode"),
            ("bad-uri", Values(("Service:Endpoint", "not-a-uri"), ("Service:RetryLimit", "3"), ("Service:Mode", "write")), "rejected|invalid-uri:Service:Endpoint"),
            ("bad-limit", Values(("Service:Endpoint", "https://api.example.test"), ("Service:RetryLimit", "0"), ("Service:Mode", "write")), "rejected|invalid-range:Service:RetryLimit"),
            ("bad-mode", Values(("Service:Endpoint", "https://api.example.test"), ("Service:RetryLimit", "3"), ("Service:Mode", "admin")), "rejected|invalid-mode:Service:Mode")
        };

        foreach (var test in cases)
        {
            var actual = ConfigurationPolicy.Canonical(ConfigurationPolicy.Load(test.Values));
            if (!string.Equals(actual, test.Expected, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"{test.Name}: expected {test.Expected}, got {actual}.");
                return 1;
            }
        }

        Console.WriteLine($"Configuration firewall: {cases.Length} cases passed.");
        return 0;
    }

    private static ImmutableDictionary<string, string> Values(params (string Key, string Value)[] pairs) =>
        pairs.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}

public sealed record ConfigPresent(string Value);
public sealed record ConfigAbsent;
public readonly union ConfigValue(ConfigPresent, ConfigAbsent);
public sealed record ReadOnlyMode;
public sealed record WriteMode;
public readonly union ServiceMode(ReadOnlyMode, WriteMode);
public sealed record ServiceConfiguration(Uri Endpoint, int RetryLimit, ServiceMode Mode);
public sealed record ConfigurationLoaded(ServiceConfiguration Value);
public sealed record ConfigurationRejected(ImmutableArray<ConfigurationIssue> Issues);
public readonly union ConfigurationLoad(ConfigurationLoaded, ConfigurationRejected);
public sealed record ConfigurationIssue(string Code, string Key);

public static class ConfigurationPolicy
{
    public static ConfigurationLoad Load(ImmutableDictionary<string, string> values)
    {
        var issues = ImmutableArray.CreateBuilder<ConfigurationIssue>();
        var endpoint = ParseEndpoint(Read(values, "Service:Endpoint"), issues);
        var retryLimit = ParseRetryLimit(Read(values, "Service:RetryLimit"), issues);
        var mode = ParseMode(Read(values, "Service:Mode"), issues);
        return issues.Count == 0
            ? new ConfigurationLoaded(new ServiceConfiguration(endpoint, retryLimit, mode))
            : new ConfigurationRejected(issues.ToImmutable());
    }

    public static string Canonical(ConfigurationLoad result) => result switch
    {
        ConfigurationLoaded loaded => $"loaded|{loaded.Value.Endpoint}|{loaded.Value.RetryLimit}|{ModeName(loaded.Value.Mode)}",
        ConfigurationRejected rejected => $"rejected|{string.Join(';', rejected.Issues.Select(issue => $"{issue.Code}:{issue.Key}"))}"
    };

    private static ConfigValue Read(ImmutableDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? new ConfigPresent(value) : new ConfigAbsent();

    private static Uri ParseEndpoint(ConfigValue value, ImmutableArray<ConfigurationIssue>.Builder issues) => value switch
    {
        ConfigPresent present when Uri.TryCreate(present.Value, UriKind.Absolute, out var endpoint) && endpoint.Scheme is "https" or "http" => endpoint,
        ConfigPresent => InvalidUri(issues),
        ConfigAbsent => MissingUri(issues)
    };

    private static int ParseRetryLimit(ConfigValue value, ImmutableArray<ConfigurationIssue>.Builder issues) => value switch
    {
        ConfigPresent present when int.TryParse(present.Value, out var retryLimit) && retryLimit is >= 1 and <= 10 => retryLimit,
        ConfigPresent => InvalidRange(issues),
        ConfigAbsent => MissingRetryLimit(issues)
    };

    private static ServiceMode ParseMode(ConfigValue value, ImmutableArray<ConfigurationIssue>.Builder issues) => value switch
    {
        ConfigPresent { Value: "read-only" } => new ReadOnlyMode(),
        ConfigPresent { Value: "write" } => new WriteMode(),
        ConfigPresent => InvalidMode(issues),
        ConfigAbsent => MissingMode(issues)
    };

    private static Uri InvalidUri(ImmutableArray<ConfigurationIssue>.Builder issues) { issues.Add(new ConfigurationIssue("invalid-uri", "Service:Endpoint")); return new Uri("https://invalid.example.test"); }
    private static Uri MissingUri(ImmutableArray<ConfigurationIssue>.Builder issues) { issues.Add(new ConfigurationIssue("missing", "Service:Endpoint")); return new Uri("https://missing.example.test"); }
    private static int InvalidRange(ImmutableArray<ConfigurationIssue>.Builder issues) { issues.Add(new ConfigurationIssue("invalid-range", "Service:RetryLimit")); return 0; }
    private static int MissingRetryLimit(ImmutableArray<ConfigurationIssue>.Builder issues) { issues.Add(new ConfigurationIssue("missing", "Service:RetryLimit")); return 0; }
    private static ServiceMode InvalidMode(ImmutableArray<ConfigurationIssue>.Builder issues) { issues.Add(new ConfigurationIssue("invalid-mode", "Service:Mode")); return new ReadOnlyMode(); }
    private static ServiceMode MissingMode(ImmutableArray<ConfigurationIssue>.Builder issues) { issues.Add(new ConfigurationIssue("missing", "Service:Mode")); return new ReadOnlyMode(); }
    private static string ModeName(ServiceMode mode) => mode switch { ReadOnlyMode => "ReadOnly", WriteMode => "Write" };
}
