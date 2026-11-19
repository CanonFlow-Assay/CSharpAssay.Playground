using System.Text.Json;
using Microsoft.Data.Sqlite;
using Playground.Saga.Demo;
using Playground.Saga.Domain;

namespace Playground.Saga.Tests;

public sealed class OutboxTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("saga-outbox-");

    [Fact]
    public void RefundSurvivesTwoRestartsAndProducesOneSimulatedEffect()
    {
        var trace = TraceFor("confirmed-nonpayment");
        var recovery = OutboxExercise.Run(trace, directory.FullName);

        Assert.Equal(trace.TransferId + ":refund", recovery.RefundKey);
        Assert.Equal(nameof(RefundPending), recovery.PendingStateAfterReopen);
        Assert.Equal(new DeliveryCount(1, 1), recovery.AfterLostAcknowledgement);
        Assert.Equal(new DeliveryCount(2, 1), recovery.AfterRetry);
        Assert.Equal(nameof(Refunded), recovery.FinalState);
        Assert.False(recovery.PendingIntentRemains);
        var refund = Assert.Single(trace.Entries, entry => entry.Observation.Action == nameof(Refund));
        Assert.Equal(refund.Sha256, recovery.DecisionSha256);
    }

    [Fact]
    public void FailedIntentInsertRollsBackDecisionAndJournal()
    {
        var run = TraceFor("late-success");
        var debit = Assert.Single(run.Entries, entry => entry.Observation.Action == nameof(Debit));
        var store = new SagaOutbox(Path.Combine(directory.FullName, "saga.sqlite"));
        var journal = new EvidenceJournal();
        foreach (var entry in run.Entries.Take(debit.Sequence))
        {
            journal.Append(entry.Observation);
            store.Record(run.TransferId, entry);
        }

        store.Record(run.TransferId, debit);
        var pending = Assert.IsType<DispatchIntent>(store.Next());
        journal.Append(debit.Observation with { Time = debit.Observation.Time.AddSeconds(1) });
        Assert.Throws<SqliteException>(() => store.Record(run.TransferId, journal.Entries[^1]));
        Assert.Equal(debit.Sha256, store.ReadLatest(run.TransferId).Sha256);
        Assert.Equal(pending, store.Next());
    }

    [Fact]
    public void DisputeBlocksAnUndeliveredRefund()
    {
        var trace = TraceFor("conflicting-nonpayment");
        var store = new SagaOutbox(Path.Combine(directory.FullName, "saga.sqlite"));
        var partner = new SimulatedPartner(Path.Combine(directory.FullName, "partner.sqlite"));
        var refund = Assert.Single(trace.Entries, entry => entry.Observation.Action == nameof(Refund));
        foreach (var entry in trace.Entries.Take(refund.Sequence - 1))
        {
            store.Record(trace.TransferId, entry);
            store.DispatchOne(partner);
        }

        store.Record(trace.TransferId, refund);
        var intent = Assert.IsType<DispatchIntent>(store.Next());
        Assert.Equal(nameof(Refund), intent.Kind);
        store.Record(trace.TransferId, trace.Entries[^1]);
        Assert.Equal(nameof(Disputed), store.ReadLatest(trace.TransferId).Observation.State);
        Assert.False(store.DispatchOne(partner));
        Assert.Equal(new DeliveryCount(0, 0), partner.Count(intent.Key));
    }

    [Fact]
    public void PartnerRejectsDifferentPayloadUnderPreviouslyAppliedKey()
    {
        var partner = new SimulatedPartner(Path.Combine(directory.FullName, "partner.sqlite"));
        var intent = new DispatchIntent("example:refund", nameof(Refund), """{"Usd":100}""");
        partner.Deliver(intent);
        Assert.Throws<InvalidDataException>(() => partner.Deliver(intent with { Payload = """{"Usd":101}""" }));
        Assert.Equal(new DeliveryCount(1, 1), partner.Count(intent.Key));
    }

    [Fact]
    public void JournalRejectsBackwardsObservationTime()
    {
        var observation = TraceFor("late-success").Entries[0].Observation;
        var journal = new EvidenceJournal();
        journal.Append(observation);
        Assert.Throws<InvalidDataException>(() => journal.Append(observation with { Time = observation.Time.AddTicks(-1) }));
        Assert.Single(journal.Entries);
    }

    [Fact]
    public void JournalHashNormalizesObjectKeyOrder()
    {
        var observation = TraceFor("late-success").Entries[0].Observation;
        using var first = JsonDocument.Parse("""{"b":[true,{"d":4,"c":"test"}],"a":1}""");
        using var second = JsonDocument.Parse("""{"a":1,"b":[true,{"c":"test","d":4}]}""");
        var journal = new EvidenceJournal();
        journal.Append(observation with { Input = first.RootElement });
        var reordered = journal.Entries[0] with { Observation = observation with { Input = second.RootElement } };
        Assert.True(EvidenceJournal.Verify([reordered], journal.Head));
    }

    public void Dispose() => directory.Delete(recursive: true);

    private static Trace TraceFor(string scenario) => Assert.Single(Scenarios.Run(), trace => trace.Scenario == scenario);
}
