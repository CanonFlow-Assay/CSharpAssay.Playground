using System.Collections.Immutable;
using System.Text.Json;
using Playground.Saga.Domain;

namespace Playground.Saga.Demo;

public sealed record Trace(string Scenario, string TransferId, string FinalState, string HeadSha256,
    ImmutableArray<JournalEntry> Entries);

public static class Scenarios
{
    public static ImmutableArray<Trace> Run() =>
    [
        Funded("late-success", bankPays: true),
        Funded("confirmed-nonpayment", bankPays: false),
        BeforeDebit("missing-fx", withQuote: false),
        BeforeDebit("sanctions-hold", withQuote: true)
    ];

    private static Trace Funded(string name, bool bankPays)
    {
        var trace = new ScenarioRun(name);
        trace.Observe("FX", new QuoteReceived(trace.Id, trace.Quote), 1);
        trace.Observe("Compliance", new ScreeningCleared(trace.Key("screen"), "screening-clear-100"), 2);
        trace.Observe("Ledger", new DebitPosted(trace.Key("debit"), "debit-100"), 3);
        trace.Observe("Treasury", new EscrowHeld(trace.Key("escrow"), "escrow-100"), 4);
        trace.Observe("Clock", new TimedOut(), 30);
        trace.Observe("SWIFT", new CreditStillUnknown(trace.Key("payout")), 31);
        if (bankPays)
        {
            trace.Observe("SWIFT", new CreditConfirmed(trace.Key("payout"), "credit-100"), 35);
            trace.Observe("Treasury", new EscrowReleased(trace.Key("release-settled")), 36);
            trace.Observe("SWIFT", new CreditConfirmed(trace.Key("payout"), "credit-100"), 37);
        }
        else
        {
            trace.Observe("SWIFT", new CreditRejected(trace.Key("payout"), "terminal-rejection-100"), 35);
            trace.Observe("Treasury", new EscrowReleased(trace.Key("release-unwind")), 36);
            trace.Observe("Ledger", new DebitRefunded(trace.Key("refund"), "refund-100"), 37);
            trace.Observe("Ledger", new DebitRefunded(trace.Key("refund"), "refund-100"), 38);
        }

        return trace.Finish();
    }

    private static Trace BeforeDebit(string name, bool withQuote)
    {
        var trace = new ScenarioRun(name);
        if (withQuote)
        {
            trace.Observe("FX", new QuoteReceived(trace.Id, trace.Quote), 1);
            trace.Observe("Compliance", new ScreeningHeld(trace.Key("screen"), "review-100"), 2);
            trace.Observe("Clock", new TimedOut(), 30);
        }
        else
        {
            trace.Observe("FX", new QuoteUnavailable(trace.Id), 1);
        }

        return trace.Finish();
    }

    private sealed class ScenarioRun
    {
        private static readonly DateTimeOffset Start = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        private readonly EvidenceJournal journal = new();
        private readonly string name;
        private Decision decision;

        public ScenarioRun(string name)
        {
            this.name = name;
            Id = "transfer-" + name;
            var request = new Transfer(Id, 100_000m);
            decision = PaymentSaga.Start(request);
            Record("Agent simulation", "TransferRequested", request, Start);
        }

        public string Id { get; }

        public Quote Quote => new(Key("fx"), "USD", "EUR", 0.92m, Start.AddMinutes(2));

        public string Key(string operation) => Id + ":" + operation;

        public void Observe(string partner, PaymentEvent message, int seconds)
        {
            var time = Start.AddSeconds(seconds);
            decision = PaymentSaga.Apply(decision.State, message, time);
            var input = message.Value ?? throw new InvalidOperationException("A simulated event is required.");
            Record(partner, input.GetType().Name, input, time);
        }

        public Trace Finish() => new(name, Id, CaseName(decision.State.Value), journal.Head, journal.Entries);

        private void Record(string partner, string eventName, object payload, DateTimeOffset time)
        {
            var action = decision.Action.Value ?? throw new InvalidOperationException("A simulated decision is required.");
            journal.Append(new(time, partner, eventName, JsonSerializer.SerializeToElement(payload),
                CaseName(decision.State.Value), action.GetType().Name, JsonSerializer.SerializeToElement(action), decision.Evidence));
        }

        private static string CaseName(object? value) => value?.GetType().Name ?? throw new InvalidOperationException("A simulated state is required.");
    }
}
