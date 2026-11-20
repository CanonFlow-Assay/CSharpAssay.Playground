namespace Playground.Parity.Domain;

public readonly record struct ValidAirportCode(string Value);
public readonly record struct InvalidAirportCode;
public readonly union AirportCode(ValidAirportCode, InvalidAirportCode);

public static class Airline
{
    public static AirportCode ParseAirport(string text) => text is { Length: 3 }
        ? new ValidAirportCode(text.ToUpperInvariant())
        : new InvalidAirportCode();
}

public enum OrderSchema { Original, Relaxed }

public static class EnterpriseRules
{
    public static bool ArangetramAmount(decimal amount) => amount > 0m;

    public static bool BankingAmount(decimal amount) => amount > 0m;

    public static bool GatepassDuration(double hours) => hours is >= 1d and <= 72d;

    public static bool CanRegisterPatient(bool alreadyRegistered) => !alreadyRegistered;

    public static bool TicketAllocation(int seats, int sold) => seats >= 0 && sold >= 0 && sold <= seats;

    public static bool ExamMarks(int theory, int practical) => (long)theory + practical <= 100;

    public static bool OrderTotal(OrderSchema schema, decimal amount) => schema switch
    {
        OrderSchema.Original => amount > 0m,
        OrderSchema.Relaxed => amount >= 0m,
        _ => throw new ArgumentOutOfRangeException(nameof(schema))
    };

    public static bool DrillCustomer(int age) => age is >= 18 and < 120;

    public static bool CreditMember(int age, bool hasGuardian) => age >= 21 || hasGuardian;

    public static bool TradingRestricted(decimal accountValue, int recentDayTrades) =>
        accountValue < 25_000m && recentDayTrades >= 3;

    public static bool CanCompleteBooking(DateTimeOffset scheduled, DateTimeOffset completed) =>
        completed >= scheduled;

    public static bool CanReadMessage(DateTimeOffset sent, DateTimeOffset read) => read >= sent;

    public static bool CanDebit(decimal balance, decimal amount) => amount > 0m && amount <= balance;
}

public readonly record struct InvalidCoordinates;
public readonly union CoordinateValidation(Coordinates, InvalidCoordinates);

public sealed record Coordinates
{
    private Coordinates(double latitude, double longitude) => (Latitude, Longitude) = (latitude, longitude);

    public double Latitude { get; }
    public double Longitude { get; }

    public static CoordinateValidation Create(double latitude, double longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180
            ? new Coordinates(latitude, longitude)
            : new InvalidCoordinates();

    public bool IsWithinDeliveryRadius(Coordinates other)
    {
        const double earthRadiusKm = 6371d;
        const double deliveryRadiusKm = 0.1d;
        var latitude = Radians(other.Latitude - Latitude);
        var longitude = Radians(other.Longitude - Longitude);
        var a = Math.Pow(Math.Sin(latitude / 2d), 2d) +
            Math.Cos(Radians(Latitude)) * Math.Cos(Radians(other.Latitude)) *
            Math.Pow(Math.Sin(longitude / 2d), 2d);
        return 2d * earthRadiusKm * Math.Asin(Math.Sqrt(Math.Clamp(a, 0d, 1d))) <= deliveryRadiusKm;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180d;
}
