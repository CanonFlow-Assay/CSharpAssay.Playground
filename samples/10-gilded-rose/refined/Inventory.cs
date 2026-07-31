using System.Collections.Immutable;

namespace Playground.GildedRose.Refined;

public readonly record struct InventoryItem(
    string Name,
    int SellIn,
    int Quality);

public static class InventoryUpdate
{
    public const string AgedBrie = "Aged Brie";
    public const string BackstagePass =
        "Backstage passes to a TAFKAL80ETC concert";
    public const string Sulfuras = "Sulfuras, Hand of Ragnaros";

    public static ImmutableArray<InventoryItem> UpdateQuality(
        ImmutableArray<InventoryItem> inventory) =>
        [.. inventory.Select(UpdateItem)];

    private static InventoryItem UpdateItem(InventoryItem item)
    {
        if (item.Name == Sulfuras)
        {
            return item;
        }

        var quality = item.Name switch
        {
            AgedBrie => Increase(item.Quality, item.SellIn <= 0 ? 2 : 1),
            BackstagePass => BackstageQuality(item),
            _ => Decrease(item.Quality, item.SellIn <= 0 ? 2 : 1)
        };
        return item with
        {
            SellIn = item.SellIn - 1,
            Quality = quality
        };
    }

    private static int BackstageQuality(InventoryItem item) =>
        item.SellIn <= 0
            ? 0
            : Increase(
                item.Quality,
                item.SellIn <= 5 ? 3 : item.SellIn <= 10 ? 2 : 1);

    private static int Increase(int quality, int amount) =>
        Math.Min(50, quality + amount);

    private static int Decrease(int quality, int amount) =>
        Math.Max(0, quality - amount);
}
