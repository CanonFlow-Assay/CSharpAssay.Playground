using System.Globalization;
using System.Text.Json;
using CsvFunctional.Domain;
using ExpenseGuard.Domain;
using Inventory.Domain;
using Playground.GildedRose.Refined;
using Playground.Gof.Refined;
using Playground.Parity.Domain;
using Playground.RuleMatrix.Refined.Core;
using PollyAssay.Domain;
using Shape.Domain;

namespace Playground.Parity.Tests;

public sealed class NativeCases
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, string, string, string> Cases
    {
        get
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "native-cases.json")));
            var cases = new TheoryData<string, string, string, string>();
            foreach (var sample in document.RootElement.GetProperty("samples").EnumerateArray())
            {
                foreach (var specimen in sample.GetProperty("cases").EnumerateArray())
                {
                    cases.Add(Text(sample.GetProperty("id")), Text(specimen.GetProperty("id")),
                        specimen.GetProperty("input").GetRawText(), Text(specimen.GetProperty("expected")));
                }
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeSampleMatchesSharedBehavior(string sample, string scenario, string input, string expected)
    {
        using var document = JsonDocument.Parse(input);
        var actual = Evaluate(sample, document.RootElement);
        Assert.True(expected == actual, $"{sample}/{scenario}: expected {expected}, received {actual}");
    }

    internal static string Text(JsonElement value) => value.GetString() ?? throw new JsonException("Expected string.");
    private static string Valid(bool valid) => valid ? "valid" : "invalid";
    private static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    private static string Evaluate(string sample, JsonElement args) => sample switch
    {
        "rule-matrix" => Matrix(args[0].GetString()),
        "gilded-rose" => UpdateInventory(args),
        "gof-crosswalk" => Number(ShippingQuotes.Standard(Text(args[0]) switch
        {
            "parcel" => new Parcel(args[1].GetDecimal()),
            "freight" => new Freight(args[1].GetInt32()),
            _ => throw new JsonException("Unknown shipment.")
        })),
        "functional-shape" => SubmitOrder(args[0]),
        "decision-assist" => Advise(args),
        "csvhelper" => ConvertField(args),
        "polly-resilience" => Fetch(args),
        "inventory-reservation" => Reserve(args),
        "ef-boundaries" => MapRow(args),
        "expenseguard" => AssessExpense(args),
        "payment-saga" => SagaCases.Run(args),
        "airline" => Airline.ParseAirport(args[0].GetString() ?? string.Empty) switch
        {
            ValidAirportCode code => code.Value,
            InvalidAirportCode => "invalid"
        },
        "arangetram" => Valid(EnterpriseRules.ArangetramAmount(args[0].GetDecimal())),
        "banking" => Valid(EnterpriseRules.BankingAmount(args[0].GetDecimal())),
        "gatepass" => Valid(EnterpriseRules.GatepassDuration(args[0].GetDouble())),
        "hospital" => EnterpriseRules.CanRegisterPatient(args[0].GetBoolean()) ? "registered" : "invalid",
        "kutcheri" => Valid(EnterpriseRules.TicketAllocation(args[0].GetInt32(), args[1].GetInt32())),
        "layam" => Valid(EnterpriseRules.ExamMarks(args[0].GetInt32(), args[1].GetInt32())),
        "migration" => Valid(EnterpriseRules.OrderTotal(Text(args[0]) switch
        {
            "v1" => OrderSchema.Original,
            "v2" => OrderSchema.Relaxed,
            _ => throw new JsonException("Unknown migration.")
        }, args[1].GetDecimal())),
        "mock-drill" => Valid(EnterpriseRules.DrillCustomer(args[0].GetInt32())),
        "sangam" => Valid(EnterpriseRules.CreditMember(args[0].GetInt32(), args[1].GetBoolean())),
        "trading" => EnterpriseRules.TradingRestricted(args[0].GetDecimal(), args[1].GetInt32()) ? "restricted" : "valid",
        "urbanclub" => EnterpriseRules.CanCompleteBooking(Now, Now.AddSeconds(args[0].GetDouble())) ? "completed" : "invalid",
        "webuy" => Deliver(args[0].GetDouble()),
        "wecar" => EnterpriseRules.CanReadMessage(Now, Now.AddSeconds(args[0].GetDouble())) ? "read" : "invalid",
        "fintech-wallet" => Valid(EnterpriseRules.CanDebit(args[0].GetDecimal(), args[1].GetDecimal())),
        "functional-patterns" => Playground.Parity.Domain.Pricing.Execute(quantity => quantity.Value * 2m, Text(args[0]), args[1].GetInt32()) switch
        {
            PricedOrder order => "priced:" + Number(order.Price),
            Playground.Parity.Domain.InvalidOrder invalid => string.Join(',', invalid.Errors)
        },
        "wallet" => RunWallet(args),
        _ => throw new JsonException($"Unknown sample {sample}.")
    };

    private static string Matrix(string? input)
    {
        BoundaryText value = input is null ? new BoundaryText.Missing() : new BoundaryText.Present(input);
        return value switch
        {
            BoundaryText.Missing => "missing",
            BoundaryText.Present supplied => "present:" + RefinedProfile.Create(supplied.Value).Name,
            _ => throw new InvalidOperationException("Unknown text boundary.")
        };
    }

    private static string UpdateInventory(JsonElement args)
    {
        var name = Text(args[0]) switch
        {
            "ordinary" => "ordinary",
            "brie" => InventoryUpdate.AgedBrie,
            "pass" => InventoryUpdate.BackstagePass,
            "legendary" => InventoryUpdate.Sulfuras,
            _ => throw new JsonException("Unknown item.")
        };
        var actual = InventoryUpdate.UpdateQuality([new(name, args[1].GetInt32(), args[2].GetInt32())])[0];
        return FormattableString.Invariant($"{actual.SellIn}:{actual.Quality}");
    }

    private static string SubmitOrder(JsonElement input)
    {
        var id = new Guid("d32ca23a-b9f5-46a9-83bd-21d0c09a8ef9");
        var items = input.EnumerateArray().Select(item => new OrderLineInput("SKU", item.GetInt32()));
        return Shape.Domain.OrderDecisions.Accept(new(id, [.. items], new Option<string>.None())) switch
        {
            Result<AcceptedOrder, OrderError>.Success => "accepted",
            Result<AcceptedOrder, OrderError>.Failure { Error: OrderError.EmptyOrder } => "empty",
            Result<AcceptedOrder, OrderError>.Failure { Error: OrderError.TooManyLines } => "too-many",
            Result<AcceptedOrder, OrderError>.Failure { Error: OrderError.InvalidQuantity } => "invalid-quantity",
            _ => throw new InvalidOperationException("Unexpected order outcome.")
        };
    }

    private static TestExecution Execution(JsonElement input) => Text(input) switch
    {
        "passed" => new Passed(1),
        "failed" => new Failed(1),
        "invalid" => new InvalidExecution("invalid fixture"),
        _ => throw new JsonException("Unknown execution state.")
    };

    private static string Advise(JsonElement args) => DecisionAssist.Decide(new(
        Execution(args[1]), Execution(args[2]), args[0].GetBoolean(), args[4].GetInt32(),
        args[3].GetBoolean() ? new Probe("boundary", "REQ-1") : new NoProbe())) switch
    {
        RepairBaseline => "RepairBaseline",
        RepairCandidate => "RepairCandidate",
        ContextRequired => "ContextRequired",
        BudgetExhausted => "BudgetExhausted",
        ProbeRequested => "ProbeRequested",
        SurvivorReview => "SurvivorReview",
        NoInvestigation => "NoInvestigation"
    };

    private static string ConvertField(JsonElement args)
    {
        FieldInput field = args[0].GetString() is { } text ? new PresentField(text) : new MissingField();
        return FieldConversions.ParseAmount(field, CultureInfo.GetCultureInfo(Text(args[1]))) switch
        {
            Converted<decimal> converted => Number(converted.Value),
            ConversionProblem problem => problem.Problem.ToString().ToLowerInvariant()
        };
    }

    private static string Fetch(JsonElement args)
    {
        var kind = Text(args[0]) switch
        {
            "success" => ReplyKind.Success,
            "permanent" => ReplyKind.Permanent,
            "transient" => ReplyKind.Transient,
            _ => throw new JsonException("Unknown reply.")
        };
        var reply = new TransportReply(kind, "fixture");
        var outcome = FetchDecisions.Project(FetchDecisions.Convert(reply)).Status;
        return outcome + (FetchDecisions.ShouldRetry(reply) ? ":retry" : ":no-retry");
    }

    private static string Reserve(JsonElement args)
    {
        PriorReservation prior = args[2].ValueKind == JsonValueKind.Null
            ? new NoReservation()
            : new ExistingReservation("req", "SKU", args[2].GetInt32(), 2, 7);
        var request = new ReservationRequest("req", "SKU", args[0].GetInt32());
        return ReservationDecisions.Decide(request, new(args[1].GetInt32(), 7), prior) switch
        {
            CommitReservation receipt => FormattableString.Invariant($"commit:{receipt.Remaining}:{receipt.Version}"),
            ReplayReservation replay => FormattableString.Invariant($"replay:{replay.Receipt.Remaining}:{replay.Receipt.Version}"),
            InvalidReservation => "invalid",
            InsufficientStock insufficient => FormattableString.Invariant($"insufficient:{insufficient.Available}"),
            ReservationConflict => "conflict"
        };
    }

    private static string MapRow(JsonElement args)
    {
        EfBoundary.Domain.OrderMemo memo = args[2].GetString() is { } text
            ? new EfBoundary.Domain.MemoText(text) : new EfBoundary.Domain.NoMemo();
        return EfBoundary.Domain.OrderDecisions.FromPersisted(args[0].GetInt32(), args[1].GetInt32(), memo) switch
        {
            EfBoundary.Domain.StoredOrder stored => stored.Value.Memo switch
            {
                EfBoundary.Domain.NoMemo => "stored:none",
                EfBoundary.Domain.MemoText value => "stored:" + value.Value
            },
            EfBoundary.Domain.InvalidOrder invalid when invalid.Reason.StartsWith("Order identity", StringComparison.Ordinal) => "invalid-id",
            EfBoundary.Domain.InvalidOrder => "invalid-quantity",
            EfBoundary.Domain.MissingOrder => "missing"
        };
    }

    private static string AssessExpense(JsonElement args)
    {
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        ReceiptEvidence receipt = args[1].GetBoolean()
            ? new Receipt("receipt:fixture", new string('a', 64), Itemized: true)
            : new MissingReceipt();
        var claim = new ExpenseClaim(new(Guid.Empty), new("employee"), new(args[0].GetDecimal(), "USD"),
            Category.Other, "Fixture merchant", today, 1, receipt, new Submitted(), new NoAssessment());
        return PolicyEngine.Evaluate(claim, Policies.Demo,
            new(today, new RateMissing(), false, 0m, args[2].GetBoolean())).Decision.ToString();
    }

    private static string Deliver(double latitude) =>
        Coordinates.Create(0, 0) is Coordinates origin && Coordinates.Create(latitude, 0) is Coordinates partner &&
        origin.IsWithinDeliveryRadius(partner) ? "deliver" : "invalid";

    private static string RunWallet(JsonElement args)
    {
        var state = WalletState.Start("wallet", args[0].GetDecimal());
        var results = new List<string>();
        foreach (var operation in args[2].EnumerateArray())
        {
            var debit = Debit.Create(args[1].GetDecimal(), Text(operation[1]), operation[2].GetDecimal());
            if (debit is InvalidDebit invalid)
            {
                results.Add(invalid.Problem switch
                {
                    DebitProblem.InvalidReference => "invalid-reference",
                    DebitProblem.InvalidAmount => "invalid-amount",
                    DebitProblem.Precision => "precision",
                    DebitProblem.LimitExceeded => "limit-exceeded",
                    _ => throw new InvalidOperationException("Unexpected validation.")
                });
                continue;
            }

            var result = Wallet.Apply(state, Text(operation[0]), Assert.IsType<Debit>(debit.Value));
            state = result.State;
            results.Add(result.Outcome switch
            {
                AcceptedDebit accepted => "accepted:" + Number(accepted.Balance),
                InsufficientFunds => "insufficient-funds",
                DebitConflict => "conflict"
            });
        }
        return string.Join(',', results);
    }
}
