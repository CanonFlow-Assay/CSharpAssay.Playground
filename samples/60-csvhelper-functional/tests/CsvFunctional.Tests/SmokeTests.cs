using System.Globalization;
using CsvFunctional.Adapter;
using CsvFunctional.Domain;
using Xunit;

namespace CsvFunctional.Tests;

// Frozen minimal before selection; expanded requirements live in separate classes.
public sealed class SmokeTests
{
    [Fact]
    public void ConvertsOrdinaryWholeNumber() =>
        Assert.Equal(42, Assert.IsType<Converted<int>>(FieldConversions.ParseWhole(new PresentField("42"), CultureInfo.InvariantCulture).Value).Value);

    [Fact]
    public async Task ReadsOrdinaryRecord()
    {
        using var text = new StringReader("Id,Amount,Active,Note\n1,12.5,true,hello\n");
        var events = new List<ImportEvent>();
        await foreach (var item in new CsvImporter(CultureInfo.InvariantCulture).ReadAsync(text, TestContext.Current.CancellationToken))
        { events.Add(item); }
        var row = Assert.IsType<AcceptedRow>(Assert.Single(events).Value);
        Assert.Equal(new ImportedRecord(1, 12.5m, true, "hello"), row.Value);
        Assert.Equal(1, row.RecordNumber);
    }
}
