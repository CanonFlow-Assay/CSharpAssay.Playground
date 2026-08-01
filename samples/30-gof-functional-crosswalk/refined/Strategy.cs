namespace Playground.Gof.Refined;

public static class Pricing
{
    public static decimal Checkout(decimal subtotal, Func<decimal, decimal> discount) =>
        discount(subtotal);

    public static decimal Seasonal(decimal subtotal) =>
        decimal.Round(subtotal * 0.85m, 2);
}
