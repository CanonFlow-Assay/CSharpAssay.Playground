// Converter algorithms derived from CsvHelper, Copyright 2009-2024 Josh Close.
// Licensed under MS-PL OR Apache-2.0; this sample uses Apache-2.0. See LICENSE.CsvHelper.txt.
// Modified: typed closed outcomes, explicit missing input/culture, immutable Boolean aliases;
// no row/map dependency, object boxing, ambient culture, exception fallback, or raw-value errors.
using System.Collections.Immutable;
using System.Globalization;

namespace CsvFunctional.Domain;

public readonly union FieldInput(PresentField, MissingField);
public sealed record PresentField(string Text);
public readonly record struct MissingField;
public readonly union Conversion<T>(Converted<T>, ConversionProblem);
public sealed record Converted<T>(T Value);
public enum ConversionKind { WholeNumber, Amount, Flag }
public enum ConversionProblemKind { Missing, Invalid }
public sealed record ConversionProblem(ConversionKind Kind, ConversionProblemKind Problem);
public sealed record BooleanLiterals(ImmutableArray<string> TrueValues, ImmutableArray<string> FalseValues)
{
    public static BooleanLiterals Default { get; } = new([], []);
}

public static class FieldConversions
{
    public static Conversion<int> ParseWhole(FieldInput input, CultureInfo culture, NumberStyles styles = NumberStyles.Integer) => input switch
    {
        MissingField => new ConversionProblem(ConversionKind.WholeNumber, ConversionProblemKind.Missing),
        PresentField field => int.TryParse(field.Text, styles, culture, out var value)
            ? new Converted<int>(value) : new ConversionProblem(ConversionKind.WholeNumber, ConversionProblemKind.Invalid)
    };

    public static Conversion<decimal> ParseAmount(FieldInput input, CultureInfo culture, NumberStyles styles = NumberStyles.Number) => input switch
    {
        MissingField => new ConversionProblem(ConversionKind.Amount, ConversionProblemKind.Missing),
        PresentField field => decimal.TryParse(field.Text, styles, culture, out var value)
            ? new Converted<decimal>(value) : new ConversionProblem(ConversionKind.Amount, ConversionProblemKind.Invalid)
    };

    public static Conversion<bool> ParseFlag(FieldInput input, CultureInfo culture, BooleanLiterals literals) => input switch
    {
        MissingField => new ConversionProblem(ConversionKind.Flag, ConversionProblemKind.Missing),
        PresentField field => ParseBoolean(field.Text, culture, literals)
    };

    private static Conversion<bool> ParseBoolean(string text, CultureInfo culture, BooleanLiterals literals)
    {
        if (bool.TryParse(text, out var boolean))
        {
            return new Converted<bool>(boolean);
        }
        if (short.TryParse(text, NumberStyles.Integer, culture, out var numeric))
        {
            if (numeric == 0) { return new Converted<bool>(false); }
            if (numeric == 1) { return new Converted<bool>(true); }
        }
        var trimmed = text.Trim();
        if (!literals.TrueValues.IsDefault && literals.TrueValues.Any(value => culture.CompareInfo.Compare(value, trimmed, CompareOptions.IgnoreCase) == 0))
        {
            return new Converted<bool>(true);
        }
        if (!literals.FalseValues.IsDefault && literals.FalseValues.Any(value => culture.CompareInfo.Compare(value, trimmed, CompareOptions.IgnoreCase) == 0))
        {
            return new Converted<bool>(false);
        }
        return new ConversionProblem(ConversionKind.Flag, ConversionProblemKind.Invalid);
    }
}
