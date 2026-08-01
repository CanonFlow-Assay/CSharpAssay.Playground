using System.Collections.Immutable;
using Classic = Playground.Gof.Classic;
using Refined = Playground.Gof.Refined;
using Xunit;

namespace Playground.Gof.Tests;

public sealed class EquivalenceTests
{
    [Theory]
    [InlineData(100, 85)]
    [InlineData(19.99, 16.99)]
    public void Strategy_preserves_checkout_total(decimal subtotal, decimal expected)
    {
        var classic = Classic.ClassicPricing.Checkout(
            subtotal,
            new Classic.SeasonalDiscount());
        var refined = Refined.Pricing.Checkout(subtotal, Refined.Pricing.Seasonal);

        Assert.Equal(expected, classic);
        Assert.Equal(classic, refined);
    }

    [Fact]
    public void Builder_preserves_the_order()
    {
        var classic = new Classic.MealOrderBuilder()
            .WithMain("Risotto")
            .AddExtra("Mushrooms")
            .AddExtra("Peas")
            .Build();

        var draft = Refined.MealOrders.WithMain(Refined.MealDraft.Empty, "Risotto");
        draft = Refined.MealOrders.AddExtra(draft, "Mushrooms");
        draft = Refined.MealOrders.AddExtra(draft, "Peas");
        var refined = Refined.MealOrders.Build(draft);

        Assert.Equal("Risotto", classic.Main);
        Assert.Equal(["Mushrooms", "Peas"], classic.Extras);
        Assert.Equal(classic.Main, refined.Main);
        Assert.Equal(classic.Extras, refined.Extras);
    }

    [Theory]
    [InlineData(2, 7)]
    [InlineData(8, 16)]
    public void Visitor_preserves_parcel_quote(decimal weightKg, decimal expected)
    {
        var classic = new Classic.Parcel(weightKg)
            .Accept(new Classic.StandardShippingQuote());
        var refined = Refined.ShippingQuotes.Standard(new Refined.Parcel(weightKg));

        Assert.Equal(expected, classic);
        Assert.Equal(classic, refined);
    }

    [Theory]
    [InlineData(1, 35)]
    [InlineData(4, 140)]
    public void Visitor_preserves_freight_quote(int pallets, decimal expected)
    {
        var classic = new Classic.Freight(pallets)
            .Accept(new Classic.StandardShippingQuote());
        var refined = Refined.ShippingQuotes.Standard(new Refined.Freight(pallets));

        Assert.Equal(expected, classic);
        Assert.Equal(classic, refined);
    }

    [Fact]
    public void State_preserves_valid_transition_outcome()
    {
        var classic = new Classic.ApprovalWorkflow();
        classic.Submit();
        classic.Approve();

        var refined = Refined.Approvals.Submit(Refined.ApprovalState.Draft);
        refined = Refined.Approvals.Approve(refined);

        Assert.Equal("approved", classic.Status());
        Assert.Equal(classic.Status(), Refined.Approvals.Status(refined));
    }

    [Fact]
    public void State_preserves_rejected_early_approval()
    {
        var classic = new Classic.ApprovalWorkflow();
        classic.Approve();

        var refined = Refined.Approvals.Approve(Refined.ApprovalState.Draft);

        Assert.Equal("draft", classic.Status());
        Assert.Equal(classic.Status(), Refined.Approvals.Status(refined));
    }

    [Fact]
    public void Composite_preserves_nested_menu_total()
    {
        var classicLunch = new Classic.MenuGroup();
        classicLunch.Children.Add(new Classic.MenuItem(12.50m));
        var classicDesserts = new Classic.MenuGroup();
        classicDesserts.Children.Add(new Classic.MenuItem(6.25m));
        classicDesserts.Children.Add(new Classic.MenuItem(4.75m));
        classicLunch.Children.Add(classicDesserts);

        var refinedLunch = new Refined.MenuGroup(
            ImmutableArray.Create<Refined.MenuNode>(
                new Refined.MenuItem(12.50m),
                new Refined.MenuGroup(
                    ImmutableArray.Create<Refined.MenuNode>(
                        new Refined.MenuItem(6.25m),
                        new Refined.MenuItem(4.75m)))));

        Assert.Equal(23.50m, classicLunch.Total());
        Assert.Equal(classicLunch.Total(), Refined.Menus.Total(refinedLunch));
    }
}
