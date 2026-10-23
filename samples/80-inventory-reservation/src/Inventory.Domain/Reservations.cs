namespace Inventory.Domain;

public sealed record ReservationRequest(string RequestId, string Sku, int Quantity);
public sealed record Stock(int Available, long Version);
public readonly record struct NoReservation;
public sealed record ExistingReservation(string RequestId, string Sku, int Quantity, int Remaining, long Version);
public readonly union PriorReservation(NoReservation, ExistingReservation);
public sealed record CommitReservation(string RequestId, string Sku, int Quantity, int Remaining, long Version);
public sealed record ReplayReservation(ExistingReservation Receipt);
public sealed record InvalidReservation(string Reason);
public sealed record InsufficientStock(int Available);
public sealed record ReservationConflict(string Reason);
public readonly union ReservationDecision(CommitReservation, ReplayReservation, InvalidReservation, InsufficientStock, ReservationConflict);
public readonly record struct ValidRequest;
public readonly union RequestValidation(ValidRequest, InvalidReservation);

public static class ReservationDecisions
{
    public static RequestValidation Validate(ReservationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 100 || string.IsNullOrWhiteSpace(request.Sku))
        {
            return new InvalidReservation("A bounded request key and SKU are required.");
        }
        if (request.Quantity <= 0) { return new InvalidReservation("Quantity must be positive."); }
        return new ValidRequest();
    }

    public static ReservationDecision Decide(ReservationRequest request, Stock stock, PriorReservation prior)
    {
        if (Validate(request) is InvalidReservation invalid) { return invalid; }
        if (prior is ExistingReservation existing)
        {
            return existing.Sku == request.Sku && existing.Quantity == request.Quantity
                ? new ReplayReservation(existing)
                : new ReservationConflict("Request key was already used with different input.");
        }
        if (request.Quantity > 3)
        {
            return new InvalidReservation("Quantity cannot exceed three units.");
        }
        if (request.Quantity > stock.Available) { return new InsufficientStock(stock.Available); }
        return new CommitReservation(request.RequestId, request.Sku, request.Quantity,
            stock.Available - request.Quantity, checked(stock.Version + 1));
    }
}
