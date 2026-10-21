using System.Globalization;
using System.Text.Json;
using CsvFunctional.Adapter;
using CsvFunctional.Domain;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: csv-functional <file.csv> [culture-name]; default invariant culture.");
    return 64;
}
try
{
    var culture = CultureInfo.GetCultureInfo(args.Length == 2 ? args[1] : string.Empty);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    using var input = File.OpenText(args[0]);
    var exitCode = 0;
    await foreach (var item in new CsvImporter(culture).ReadAsync(input, cancellation.Token))
    {
        object transport = item switch
        {
            AcceptedRow row => new { status = "accepted", row = row.RecordNumber, row.Value.Id, row.Value.Amount, row.Value.Active, row.Value.Note },
            RejectedRow row => new { status = "rejected", row = row.RecordNumber, issues = row.Issues.Select(i => new { column = i.Column, code = i.Kind.ToString() }) },
            ImportFailure failure => new { status = "failed", row = failure.RecordNumber, code = failure.Kind.ToString() },
            ImportCancelled cancelled => new { status = "cancelled", row = cancelled.RecordNumber }
        };
        Console.WriteLine(JsonSerializer.Serialize(transport));
        exitCode = item switch { AcceptedRow => exitCode, RejectedRow => 2, ImportFailure => 2, ImportCancelled => 130 };
    }
    return exitCode;
}
catch (CultureNotFoundException)
{
    Console.Error.WriteLine("Unsupported culture name.");
    return 64;
}
catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("Cannot open or read CSV input.");
    return 2;
}
