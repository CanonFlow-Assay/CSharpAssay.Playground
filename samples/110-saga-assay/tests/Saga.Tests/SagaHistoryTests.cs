using Playground.Saga.Domain;

namespace Playground.Saga.Tests;

public sealed class SagaHistoryTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed record Expected(PaymentEvent Event, DateTimeOffset Time, Type State, Type Action, Type? TimeoutAction = null);

    [Theory]
    [InlineData(17)]
    [InlineData(83)]
    [InlineData(607)]
    [InlineData(2026)]
    public void GeneratedHistoriesRespectTheDeclaredMoneyMovementOrder(int seed)
    {
        var random = new Random(seed);
        for (var example = 0; example < 32; example++)
        {
            var transfer = new Transfer($"history-{seed}-{example}", random.Next(1, 100_000));
            var quote = new Quote("locked-rate", "USD", "EUR", random.Next(50, 151) / 100m, Epoch.AddMinutes(1));
            var result = PaymentSaga.Start(transfer);
            var previous = new Expected(new QuoteUnavailable("unused"), Epoch, typeof(AwaitingQuote), typeof(RequestQuote));
            var prefix = new List<string>();
            foreach (var step in Plan(transfer.Id, quote, example % 6))
            {
                var foreignKey = "foreign:" + transfer.Id;
                var foreign = Foreign(step.Event, foreignKey);
                if (random.Next(2) == 0)
                {
                    ExpectNoChange(foreign, step.Time);
                }
                if (previous.TimeoutAction is { } lookup && random.Next(2) == 0)
                {
                    prefix.Add("timeout");
                    var observed = PaymentSaga.Apply(result.State, new TimedOut(), step.Time);
                    Assert.True(observed.State.Equals(result.State) && observed.Action.Value?.GetType() == lookup,
                        Failure("timeout must preserve the state and only reconcile"));
                }

                prefix.Add(step.Event.Value?.GetType().Name ?? "empty");
                result = PaymentSaga.Apply(result.State, step.Event, step.Time);
                Assert.True(result.State.Value?.GetType() == step.State && result.Action.Value?.GetType() == step.Action,
                    Failure($"expected {step.State.Name}/{step.Action.Name}; observed {result.State.Value?.GetType().Name}/{result.Action.Value?.GetType().Name}"));
                if (random.Next(2) == 0)
                {
                    ExpectNoChange(step.Event, step.Time);
                }

                previous = step;
            }

            // Every generated route ends at a terminal state or a dispute.
            ExpectNoChange(new TimedOut(), quote.ExpiresAt.AddMinutes(1));

            void ExpectNoChange(PaymentEvent message, DateTimeOffset time)
            {
                prefix.Add("ignored:" + message.Value?.GetType().Name);
                var observed = PaymentSaga.Apply(result.State, message, time);
                Assert.True(observed.State.Equals(result.State) && observed.Action.Value is NoAction,
                    Failure("foreign or replayed message changed the state or emitted an action"));
            }

            string Failure(string detail) => $"seed={seed} example={example} route={example % 6} prefix={string.Join(",", prefix)}: {detail}";
        }
    }

    private static IEnumerable<Expected> Plan(string id, Quote quote, int path)
    {
        yield return new(new QuoteReceived(id, quote), Epoch, typeof(AwaitingScreening), typeof(Screen));
        var expiresBeforeDebit = path == 0;
        yield return new(new ScreeningCleared(id + ":screen", "clear-ref"), expiresBeforeDebit ? quote.ExpiresAt : Epoch.AddSeconds(1),
            expiresBeforeDebit ? typeof(Rejected) : typeof(AwaitingDebit), expiresBeforeDebit ? typeof(NoAction) : typeof(Debit),
            expiresBeforeDebit ? null : typeof(ReconcileDebit));
        if (expiresBeforeDebit) { yield break; }

        yield return new(new DebitPosted(id + ":debit", "debit-ref"), Epoch.AddSeconds(2), typeof(AwaitingEscrow), typeof(HoldEscrow), typeof(ReconcileEscrow));
        if (path == 1)
        {
            yield return new(new EscrowRejected(id + ":escrow", "no-hold"), Epoch.AddSeconds(3), typeof(RefundPending), typeof(Refund), typeof(ReconcileRefund));
            yield return new(new DebitRefunded(id + ":refund", "refund-ref"), Epoch.AddSeconds(4), typeof(Refunded), typeof(NoAction));
            yield break;
        }

        yield return new(new EscrowHeld(id + ":escrow", "escrow-ref"), Epoch.AddSeconds(3), typeof(PayoutPending), typeof(SubmitCredit), typeof(ReconcileCredit));
        yield return new(new CreditStillUnknown(id + ":payout"), Epoch.AddSeconds(30), typeof(PayoutPending), typeof(NoAction), typeof(ReconcileCredit));
        if (path is 2 or 3)
        {
            yield return new(new CreditConfirmed(id + ":payout", "credit-ref"), Epoch.AddSeconds(35), typeof(Settling), typeof(ReleaseEscrow), typeof(ReconcileEscrowRelease));
            yield return new(new EscrowReleased(id + ":release-settled"), Epoch.AddSeconds(36), typeof(Settled), typeof(NoAction));
            if (path == 3)
            {
                yield return new(new CreditRejected(id + ":payout", "contrary-ref"), Epoch.AddSeconds(37), typeof(Disputed), typeof(Review));
            }
            yield break;
        }

        yield return new(new CreditRejected(id + ":payout", "no-credit"), Epoch.AddSeconds(35), typeof(Unwinding), typeof(ReleaseEscrow), typeof(ReconcileEscrowRelease));
        yield return new(new EscrowReleased(id + ":release-unwind"), Epoch.AddSeconds(36), typeof(RefundPending), typeof(Refund), typeof(ReconcileRefund));
        yield return path == 4
            ? new(new DebitRefunded(id + ":refund", "refund-ref"), Epoch.AddSeconds(37), typeof(Refunded), typeof(NoAction))
            : new(new CreditConfirmed(id + ":payout", "contrary-credit"), Epoch.AddSeconds(37), typeof(Disputed), typeof(Review));
    }

    private static PaymentEvent Foreign(PaymentEvent message, string key) => message.Value switch
    {
        QuoteReceived quote => quote with { TransferId = key },
        ScreeningCleared screened => screened with { Key = key },
        DebitPosted debit => debit with { Key = key },
        EscrowHeld escrow => escrow with { Key = key },
        EscrowRejected rejection => rejection with { Key = key },
        CreditStillUnknown unknown => unknown with { Key = key },
        CreditConfirmed credit => credit with { Key = key },
        CreditRejected rejected => rejected with { Key = key },
        EscrowReleased release => release with { Key = key },
        DebitRefunded refund => refund with { Key = key },
        _ => throw new ArgumentException("The history route contains an unregistered command.", nameof(message))
    };
}
