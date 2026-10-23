using System.Collections.Immutable;
using System.Globalization;

namespace CsvFunctional.Domain;

public enum CsvIssueKind { WrongFieldCount, InvalidInteger, InvalidDecimal, InvalidBoolean }
public sealed record CsvIssue(int RecordNumber, string Column, CsvIssueKind Kind);
public sealed record ImportedRecord(int Id, decimal Amount, bool Active, string Note);
public sealed record AcceptedRow(int RecordNumber, ImportedRecord Value);
public sealed record RejectedRow(int RecordNumber, ImmutableArray<CsvIssue> Issues);
public readonly union RowOutcome(AcceptedRow, RejectedRow);
public enum ImportFailureKind { Schema, MalformedCsv, InputOutput, RowLimit, FieldLimit }
public sealed record ImportFailure(int RecordNumber, ImportFailureKind Kind);
public sealed record ImportCancelled(int RecordNumber);
public readonly union ImportEvent(AcceptedRow, RejectedRow, ImportFailure, ImportCancelled);

public static class RowDecisions
{
    public static ImmutableArray<string> Header { get; } = ["Id", "Amount", "Active", "Note"];

    public static RowOutcome Convert(int recordNumber, ImmutableArray<string> fields, CultureInfo culture)
    {
        if (fields.IsDefault || fields.Length != 4)
        {
            return new RejectedRow(recordNumber, [new(recordNumber, "row", CsvIssueKind.WrongFieldCount)]);
        }
        var id = FieldConversions.ParseWhole(new PresentField(fields[0]), culture);
        var amount = FieldConversions.ParseAmount(new PresentField(fields[1]), culture);
        var active = FieldConversions.ParseFlag(new PresentField(fields[2]), culture, BooleanLiterals.Default);
        if ((id, amount, active) is (Converted<int> i, Converted<decimal> d, Converted<bool> b))
        {
            return new AcceptedRow(recordNumber, new(i.Value, d.Value, b.Value, fields[3]));
        }
        var issues = ImmutableArray.CreateBuilder<CsvIssue>();
        if (id is ConversionProblem) { issues.Add(new(recordNumber, "Id", CsvIssueKind.InvalidInteger)); }
        if (amount is ConversionProblem) { issues.Add(new(recordNumber, "Amount", CsvIssueKind.InvalidDecimal)); }
        if (active is ConversionProblem) { issues.Add(new(recordNumber, "Active", CsvIssueKind.InvalidBoolean)); }
        return new RejectedRow(recordNumber, issues.ToImmutable());
    }
}
