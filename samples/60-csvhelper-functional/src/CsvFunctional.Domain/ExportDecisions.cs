using System.Collections.Immutable;
using System.Globalization;

namespace CsvFunctional.Domain;

public sealed record FormattedRow(ImmutableArray<string> Fields);
public readonly record struct OversizedNote;
public readonly union ExportRowOutcome(FormattedRow, OversizedNote);
public sealed record ExportedRow(int RecordNumber);
public enum ExportFailureKind { NoteLimit, RowLimit, Output }
public sealed record ExportFailure(int RecordNumber, ExportFailureKind Kind);
public sealed record ExportCancelled(int RecordNumber);
public readonly union ExportEvent(ExportedRow, ExportFailure, ExportCancelled);

public static class ExportDecisions
{
    public static ExportRowOutcome Format(ImportedRecord value, CultureInfo culture) => value.Note.Length > 1024
        ? new OversizedNote()
        : new FormattedRow([value.Id.ToString(culture), value.Amount.ToString(culture), value.Active ? "True" : "False", value.Note]);
}
