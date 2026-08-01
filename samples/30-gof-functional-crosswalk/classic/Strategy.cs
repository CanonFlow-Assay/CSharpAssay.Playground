namespace Playground.Gof.Classic;

public interface IDiscountStrategy
{
    decimal Apply(decimal subtotal);
}

public sealed class SeasonalDiscount : IDiscountStrategy
{
    public decimal Apply(decimal subtotal) => decimal.Round(subtotal * 0.85m, 2);
}

public static class ClassicPricing
{
    public static decimal Checkout(decimal subtotal, IDiscountStrategy discount) =>
        discount.Apply(subtotal);
}
