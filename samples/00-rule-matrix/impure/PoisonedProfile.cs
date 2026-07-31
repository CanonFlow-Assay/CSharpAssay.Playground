#nullable disable

using System.Collections.Generic;

namespace Playground.RuleMatrix.Impure;

#pragma warning disable CSAN0003
public sealed record PoisonedProfile
{
    public string Name { get; set; }

    public List<string> Aliases { get; set; } = new();

    public string EraseCompilerEvidence(string source) => source!;

    public string IntroduceMissingValue() => default(string);
}
#pragma warning restore CSAN0003

#nullable enable

public static class PoisonedContract
{
    public static string? Echo(string? value) => value;

    public static string AnotherMissingValue() => null;
}
