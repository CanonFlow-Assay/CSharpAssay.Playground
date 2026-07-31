using System.Collections.Immutable;
using Playground.GildedRose.Refined;
using Playground.RuleMatrix.Refined.Core;
using Xunit;

namespace Playground.Tests;

public sealed class InventoryUpdateTests
{
    [Theory]
    [InlineData("Elixir", 5, 7, 4, 6)]
    [InlineData("Elixir", 0, 7, -1, 5)]
    [InlineData("Elixir", 0, 0, -1, 0)]
    [InlineData("Aged Brie", 2, 0, 1, 1)]
    [InlineData("Backstage passes to a TAFKAL80ETC concert", 5, 40, 4, 43)]
    [InlineData("Backstage passes to a TAFKAL80ETC concert", 0, 40, -1, 0)]
    [InlineData("Sulfuras, Hand of Ragnaros", 0, 80, 0, 80)]
    [InlineData("Conjured Mana Cake", 3, 6, 2, 5)]
    public void Refined_update_preserves_characterized_legacy_behavior(
        string name,
        int sellIn,
        int quality,
        int expectedSellIn,
        int expectedQuality)
    {
        var original = ImmutableArray.Create(
            new InventoryItem(name, sellIn, quality));

        var updated = InventoryUpdate.UpdateQuality(original);

        Assert.Equal(sellIn, original[0].SellIn);
        Assert.Equal(quality, original[0].Quality);
        Assert.Equal(expectedSellIn, updated[0].SellIn);
        Assert.Equal(expectedQuality, updated[0].Quality);
    }

    [Fact]
    public void Domain_state_represents_missing_and_present_values_explicitly()
    {
        BoundaryText missing = new BoundaryText.Missing();
        BoundaryText present = new BoundaryText.Present("safe");

        Assert.IsType<BoundaryText.Missing>(missing);
        Assert.Equal(
            "safe",
            Assert.IsType<BoundaryText.Present>(present).Value);
    }
}
