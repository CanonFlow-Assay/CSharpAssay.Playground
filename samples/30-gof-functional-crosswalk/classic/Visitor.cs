namespace Playground.Gof.Classic;

public interface IShippingQuoteVisitor
{
    decimal Visit(Parcel parcel);

    decimal Visit(Freight freight);
}

public interface IShipment
{
    decimal Accept(IShippingQuoteVisitor visitor);
}

public sealed record Parcel(decimal WeightKg) : IShipment
{
    public decimal Accept(IShippingQuoteVisitor visitor) => visitor.Visit(this);
}

public sealed record Freight(int Pallets) : IShipment
{
    public decimal Accept(IShippingQuoteVisitor visitor) => visitor.Visit(this);
}

public sealed class StandardShippingQuote : IShippingQuoteVisitor
{
    public decimal Visit(Parcel parcel) => 4m + parcel.WeightKg * 1.5m;

    public decimal Visit(Freight freight) => 35m * freight.Pallets;
}
