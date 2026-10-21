using System.Collections.Immutable;
using System.Globalization;
using CsvFunctional.Domain;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using Xunit;

namespace CsvFunctional.Tests;

public sealed class ConversionTests
{
    [Theory]
    [InlineData("-2147483648")]
    [InlineData("2147483647")]
    [InlineData("0")]
    [InlineData("  +42  ")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("1,000")]
    [InlineData("1.0")]
    [InlineData("")]
    [InlineData("secret-invalid")]
    public void WholeNumberParity(string text) => Parity(
        new Int32Converter(), text, CultureInfo.InvariantCulture,
        FieldConversions.ParseWhole(new PresentField(text), CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("1,234.50", "en-US")]
    [InlineData("1.234,50", "de-DE")]
    [InlineData("-0.25", "en-US")]
    [InlineData("79228162514264337593543950335", "en-US")]
    [InlineData("79228162514264337593543950336", "en-US")]
    [InlineData("1e2", "en-US")]
    [InlineData("", "en-US")]
    [InlineData("secret-invalid", "de-DE")]
    public void AmountParity(string text, string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        Parity(new DecimalConverter(), text, culture, FieldConversions.ParseAmount(new PresentField(text), culture));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" false ")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData(" +1 ")]
    [InlineData("-0")]
    [InlineData("2")]
    [InlineData("32768")]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData("secret-invalid")]
    public void FlagParity(string text) => Parity(new BooleanConverter(), text, CultureInfo.InvariantCulture,
        FieldConversions.ParseFlag(new PresentField(text), CultureInfo.InvariantCulture, BooleanLiterals.Default));

    [Theory]
    [InlineData(" yes ", true)]
    [InlineData("YES", true)]
    [InlineData("NO", false)]
    [InlineData("both", true)]
    [InlineData("false", false)]
    public void AliasPrecedenceMatchesUpstream(string text, bool expected)
    {
        var literals = new BooleanLiterals(["yes", "both", "false"], ["no", "both"]);
        var value = FieldConversions.ParseFlag(new PresentField(text), CultureInfo.InvariantCulture, literals);
        Parity(new BooleanConverter(), text, CultureInfo.InvariantCulture, value, literals: literals);
        Assert.Equal(expected, Assert.IsType<Converted<bool>>(value.Value).Value);
    }

    [Fact]
    public void ExplicitNumberStylesMatchUpstream()
    {
        var culture = CultureInfo.InvariantCulture;
        Parity(new Int32Converter(), "FF", culture, FieldConversions.ParseWhole(new PresentField("FF"), culture, NumberStyles.HexNumber), NumberStyles.HexNumber);
        Parity(new DecimalConverter(), "1e2", culture, FieldConversions.ParseAmount(new PresentField("1e2"), culture, NumberStyles.Float), NumberStyles.Float);
    }

    [Fact]
    public void MissingInputsAreTypedAndDistinctFromInvalidText()
    {
        Assert.Equal(new ConversionProblem(ConversionKind.WholeNumber, ConversionProblemKind.Missing), FieldConversions.ParseWhole(new MissingField(), CultureInfo.InvariantCulture).Value);
        Assert.Equal(new ConversionProblem(ConversionKind.Amount, ConversionProblemKind.Missing), FieldConversions.ParseAmount(new MissingField(), CultureInfo.InvariantCulture).Value);
        Assert.Equal(new ConversionProblem(ConversionKind.Flag, ConversionProblemKind.Missing), FieldConversions.ParseFlag(new MissingField(), CultureInfo.InvariantCulture, BooleanLiterals.Default).Value);
        Assert.Equal(new ConversionProblem(ConversionKind.WholeNumber, ConversionProblemKind.Invalid), FieldConversions.ParseWhole(new PresentField(""), CultureInfo.InvariantCulture).Value);
    }

    [Fact]
    public void DefaultAliasArraysAreEmptyRatherThanAnException() =>
        Assert.IsType<ConversionProblem>(FieldConversions.ParseFlag(new PresentField("yes"), CultureInfo.InvariantCulture, new(default, default)).Value);

    [Fact]
    public void NumericFlagUsesExplicitCulture()
    {
        var configured = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        configured.NumberFormat.PositiveSign = "positive";
        Assert.True(Assert.IsType<Converted<bool>>(FieldConversions.ParseFlag(new PresentField("positive1"), configured, BooleanLiterals.Default).Value).Value);
        // Upstream's short.TryParse uses ambient culture. This is a declared
        // semantic difference, so this case is deliberately not labelled parity.
        Assert.IsType<ConversionProblem>(FieldConversions.ParseFlag(new PresentField("positive1"), CultureInfo.InvariantCulture, BooleanLiterals.Default).Value);
    }

    [Fact]
    public void RejectsAllInvalidFieldsInStableColumnOrder()
    {
        var result = RowDecisions.Convert(7, ["secret-id", "secret-amount", "secret-flag", "note"], CultureInfo.InvariantCulture);
        var rejection = Assert.IsType<RejectedRow>(result.Value);
        Assert.Equal(7, rejection.RecordNumber);
        Assert.Equal(["Id", "Amount", "Active"], rejection.Issues.Select(i => i.Column));
        Assert.Equal([CsvIssueKind.InvalidInteger, CsvIssueKind.InvalidDecimal, CsvIssueKind.InvalidBoolean], rejection.Issues.Select(i => i.Kind));
        Assert.All(rejection.Issues, i => Assert.Equal(7, i.RecordNumber));
        Assert.DoesNotContain("secret", rejection.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void WrongWidthIsExplicit(int width)
    {
        var result = RowDecisions.Convert(3, Enumerable.Repeat("1", width).ToImmutableArray(), CultureInfo.InvariantCulture);
        var issue = Assert.Single(Assert.IsType<RejectedRow>(result.Value).Issues);
        Assert.Equal(new CsvIssue(3, "row", CsvIssueKind.WrongFieldCount), issue);
    }

    [Fact]
    public void UninitializedRowIsRejected() =>
        Assert.IsType<RejectedRow>(RowDecisions.Convert(1, default, CultureInfo.InvariantCulture).Value);

    [Fact]
    public void DomainAssemblyHasNoCsvOrIoDependency()
    {
        var references = typeof(FieldConversions).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain("CsvHelper", references);
        Assert.DoesNotContain("CsvFunctional.Adapter", references);
        Assert.DoesNotContain(typeof(FieldConversions).Assembly.GetTypes().SelectMany(t => t.GetFields()), f => typeof(TextReader).IsAssignableFrom(f.FieldType));
    }

    private static void Parity<T>(ITypeConverter converter, string text, CultureInfo culture, Conversion<T> actual,
        NumberStyles? styles = null, BooleanLiterals? literals = null)
    {
        using var input = new StringReader("");
        using var reader = new CsvReader(input, culture);
        var member = typeof(OracleRecord).GetProperty(nameof(OracleRecord.Value)) ?? throw new InvalidOperationException("Oracle property missing.");
        var map = new MemberMapData(member);
        map.TypeConverterOptions.CultureInfo = culture;
        map.TypeConverterOptions.NumberStyles = styles;
        if (literals is not null)
        {
            map.TypeConverterOptions.BooleanTrueValues.AddRange(literals.TrueValues);
            map.TypeConverterOptions.BooleanFalseValues.AddRange(literals.FalseValues);
        }
        try
        {
            var expected = Assert.IsType<T>(converter.ConvertFromString(text, reader, map));
            Assert.Equal(expected, Assert.IsType<Converted<T>>(actual.Value).Value);
        }
        catch (TypeConverterException)
        {
            Assert.Equal(ConversionProblemKind.Invalid, Assert.IsType<ConversionProblem>(actual.Value).Problem);
        }
    }

    private sealed record OracleRecord(string Value);
}
