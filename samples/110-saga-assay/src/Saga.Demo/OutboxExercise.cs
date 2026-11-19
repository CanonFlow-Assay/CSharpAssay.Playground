using Playground.Saga.Domain;

namespace Playground.Saga.Demo;

public sealed record RecoveryObservation(string RefundKey, string PendingStateAfterReopen, string DecisionSha256,
    DeliveryCount AfterLostAcknowledgement, DeliveryCount AfterRetry, string FinalState, bool PendingIntentRemains);

public static class OutboxExercise
{
    public static RecoveryObservation Run(Trace trace, string directory)
    {
        Directory.CreateDirectory(directory);
        var sagaPath = Path.Combine(directory, "saga.sqlite");
        var partnerPath = Path.Combine(directory, "partner.sqlite");
        if (File.Exists(sagaPath) || File.Exists(partnerPath))
        {
            throw new IOException("Use a fresh outbox exercise directory.");
        }

        var refund = trace.Entries.Single(entry => entry.Observation.Action == nameof(Refund));
        var outbox = new SagaOutbox(sagaPath);
        var partner = new SimulatedPartner(partnerPath);
        foreach (var entry in trace.Entries.TakeWhile(entry => entry.Sequence < refund.Sequence))
        {
            outbox.Record(trace.TransferId, entry);
            outbox.DispatchOne(partner);
        }

        outbox.Record(trace.TransferId, refund);
        // All connections used by Record have closed. A replacement worker opens both durable stores.
        outbox = new SagaOutbox(sagaPath);
        partner = new SimulatedPartner(partnerPath);
        var persisted = outbox.ReadLatest(trace.TransferId);
        if (persisted.Sha256 != refund.Sha256 || outbox.Next() is not { Kind: nameof(Refund) } intent)
        {
            throw new InvalidDataException("Refund intent and decision must survive together.");
        }

        // The partner commits the effect. The worker stops before recording the acknowledgement.
        partner.Deliver(intent);
        var afterLostAck = partner.Count(intent.Key);
        outbox = new SagaOutbox(sagaPath);
        partner = new SimulatedPartner(partnerPath);
        if (!outbox.DispatchOne(partner))
        {
            throw new InvalidDataException("Refund intent disappeared before acknowledgement.");
        }

        foreach (var entry in trace.Entries.Skip(refund.Sequence))
        {
            outbox.Record(trace.TransferId, entry);
        }

        return new(intent.Key, persisted.Observation.State, persisted.Sha256, afterLostAck, partner.Count(intent.Key),
            outbox.ReadLatest(trace.TransferId).Observation.State, outbox.Next() is not null);
    }
}
