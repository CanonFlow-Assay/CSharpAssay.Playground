namespace Playground.Gof.Refined;

public abstract record Shipment;

public sealed record Parcel(decimal WeightKg) : Shipment;

public sealed record Freight(int Pallets) : Shipment;

public static class ShippingQuotes
{
    public static decimal Standard(Shipment shipment) => shipment switch
    {
        Parcel parcel => 4m + parcel.WeightKg * 1.5m,
        Freight freight => 35m * freight.Pallets,
        _ => throw new ArgumentOutOfRangeException(nameof(shipment))
    };
}
