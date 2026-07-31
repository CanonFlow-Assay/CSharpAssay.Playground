using GildedRoseKata;

var cases = new[]
{
    new Expected("normal before expiry", "Elixir", 5, 7, 4, 6),
    new Expected("normal after expiry", "Elixir", 0, 7, -1, 5),
    new Expected("quality floor", "Elixir", 0, 0, -1, 0),
    new Expected("aged brie", "Aged Brie", 2, 0, 1, 1),
    new Expected("backstage five days", "Backstage passes to a TAFKAL80ETC concert", 5, 40, 4, 43),
    new Expected("backstage expired", "Backstage passes to a TAFKAL80ETC concert", 0, 40, -1, 0),
    new Expected("sulfuras", "Sulfuras, Hand of Ragnaros", 0, 80, 0, 80),
    new Expected("legacy conjured", "Conjured Mana Cake", 3, 6, 2, 5)
};

foreach (var expected in cases)
{
    var item = new Item
    {
        Name = expected.Name,
        SellIn = expected.SellIn,
        Quality = expected.Quality
    };
    new GildedRose([item]).UpdateQuality();
    if (item.SellIn != expected.ExpectedSellIn ||
        item.Quality != expected.ExpectedQuality)
    {
        throw new InvalidOperationException(
            "Characterization failed: " + expected.Case);
    }
}

Console.WriteLine("GildedRose upstream characterization: 8/8 passed");

internal sealed record Expected(
    string Case,
    string Name,
    int SellIn,
    int Quality,
    int ExpectedSellIn,
    int ExpectedQuality);
