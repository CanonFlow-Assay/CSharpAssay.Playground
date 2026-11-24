namespace VersionedWebhook;

public static class Program
{
    public static int Main(string[] args) => args switch
    {
        ["--verify"] => Verify(),
        _ => Usage()
    };

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: VersionedWebhook --verify");
        return 64;
    }

    private static int Verify()
    {
        var cases = new (string Name, WebhookFacts Facts, string Expected)[]
        {
            ("v1-paid", Facts("v1", "invoice.paid", new SignatureVerified(), new FirstDelivery()), "accepted|record-payment|inv-42|1250"),
            ("v2-paid", Facts("v2", "invoice.paid", new SignatureVerified(), new FirstDelivery()), "accepted|record-payment|inv-42|1250"),
            ("missing-signature", Facts("v1", "invoice.paid", new SignatureMissing(), new FirstDelivery()), "rejected|missing-signature"),
            ("bad-signature", Facts("v1", "invoice.paid", new SignatureInvalid(), new FirstDelivery()), "rejected|invalid-signature"),
            ("duplicate", Facts("v1", "invoice.paid", new SignatureVerified(), new DuplicateDelivery()), "rejected|duplicate-delivery"),
            ("unknown-version", Facts("v3", "invoice.paid", new SignatureVerified(), new FirstDelivery()), "rejected|unsupported-version"),
            ("unknown-event", Facts("v1", "invoice.deleted", new SignatureVerified(), new FirstDelivery()), "rejected|unsupported-event")
        };

        foreach (var test in cases)
        {
            var actual = WebhookPolicy.Canonical(WebhookPolicy.Route(test.Facts));
            if (!string.Equals(actual, test.Expected, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"{test.Name}: expected {test.Expected}, got {actual}.");
                return 1;
            }
        }

        Console.WriteLine($"Versioned webhook policy: {cases.Length} cases passed.");
        return 0;
    }

    private static WebhookFacts Facts(string version, string eventType, SignatureStatus signature, DeliveryStatus delivery) =>
        new(new WebhookEnvelope(version, eventType, "delivery-42", "inv-42", 1250), signature, delivery);
}

public sealed record WebhookEnvelope(string Version, string EventType, string DeliveryId, string InvoiceId, int AmountCents);
public sealed record SignatureVerified;
public sealed record SignatureMissing;
public sealed record SignatureInvalid;
public readonly union SignatureStatus(SignatureVerified, SignatureMissing, SignatureInvalid);
public sealed record FirstDelivery;
public sealed record DuplicateDelivery;
public readonly union DeliveryStatus(FirstDelivery, DuplicateDelivery);
public sealed record WebhookFacts(WebhookEnvelope Envelope, SignatureStatus Signature, DeliveryStatus Delivery);
public sealed record RecordPayment(string InvoiceId, int AmountCents);
public sealed record WebhookAccepted(RecordPayment Command);
public sealed record WebhookRejected(string Code);
public readonly union WebhookRoute(WebhookAccepted, WebhookRejected);

public static class WebhookPolicy
{
    public static WebhookRoute Route(WebhookFacts facts) => facts.Signature switch
    {
        SignatureMissing => new WebhookRejected("missing-signature"),
        SignatureInvalid => new WebhookRejected("invalid-signature"),
        SignatureVerified => RouteVerified(facts)
    };

    public static string Canonical(WebhookRoute route) => route switch
    {
        WebhookAccepted accepted => $"accepted|record-payment|{accepted.Command.InvoiceId}|{accepted.Command.AmountCents}",
        WebhookRejected rejected => $"rejected|{rejected.Code}"
    };

    private static WebhookRoute RouteVerified(WebhookFacts facts) => facts.Delivery switch
    {
        DuplicateDelivery => new WebhookRejected("duplicate-delivery"),
        FirstDelivery => RouteFresh(facts.Envelope)
    };

    private static WebhookRoute RouteFresh(WebhookEnvelope envelope) => envelope.Version switch
    {
        "v1" or "v2" when envelope.EventType == "invoice.paid" && envelope.AmountCents >= 0 => new WebhookAccepted(new RecordPayment(envelope.InvoiceId, envelope.AmountCents)),
        "v1" or "v2" when envelope.EventType != "invoice.paid" => new WebhookRejected("unsupported-event"),
        "v1" or "v2" => new WebhookRejected("invalid-payload"),
        _ => new WebhookRejected("unsupported-version")
    };
}
