namespace Playground.Gof.Refined;

public readonly union Shipment(Parcel, Freight);

public sealed record Parcel(decimal WeightKg);

public sealed record Freight(int Pallets);

public static class ShippingQuotes
{
    public static decimal Standard(Shipment shipment) => shipment switch
    {
        Parcel parcel => 4m + parcel.WeightKg * 1.5m,
        Freight freight => 35m * freight.Pallets
    };
}
