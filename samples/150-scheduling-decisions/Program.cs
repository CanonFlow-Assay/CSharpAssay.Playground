using System.Collections.Immutable;

namespace SchedulingDecisions;

public static class Program
{
    public static int Main(string[] args) => args switch
    {
        ["--verify"] => Verify(),
        _ => Usage()
    };

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: SchedulingDecisions --verify");
        return 64;
    }

    private static int Verify()
    {
        var request = new ScheduleRequest(new DateOnly(2026, 11, 1), new TimeOnly(9, 30), "America/New_York");
        var cases = new (string Name, LocalTimeFacts Facts, string Expected)[]
        {
            ("valid", new LocalTimeFacts(new ZoneKnown("America/New_York"), new ValidLocalTime(TimeSpan.FromHours(-5))), "scheduled|2026-11-01T14:30:00.0000000+00:00"),
            ("skipped", new LocalTimeFacts(new ZoneKnown("America/New_York"), new SkippedLocalTime()), "rejected|nonexistent-local-time"),
            ("ambiguous", new LocalTimeFacts(new ZoneKnown("America/New_York"), new AmbiguousLocalTime([TimeSpan.FromHours(-4), TimeSpan.FromHours(-5)])), "needs-choice|-04:00,-05:00"),
            ("unknown-zone", new LocalTimeFacts(new ZoneUnknown("Mars/Phobos"), new SkippedLocalTime()), "rejected|unknown-zone:Mars/Phobos")
        };

        foreach (var test in cases)
        {
            var actual = SchedulingPolicy.Canonical(SchedulingPolicy.Decide(request, test.Facts));
            if (!string.Equals(actual, test.Expected, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"{test.Name}: expected {test.Expected}, got {actual}.");
                return 1;
            }
        }

        Console.WriteLine($"Scheduling decisions: {cases.Length} cases passed.");
        return 0;
    }
}

public sealed record ScheduleRequest(DateOnly Date, TimeOnly Time, string ZoneId);
public sealed record ZoneKnown(string Id);
public sealed record ZoneUnknown(string Id);
public readonly union ZoneFact(ZoneKnown, ZoneUnknown);
public sealed record ValidLocalTime(TimeSpan Offset);
public sealed record SkippedLocalTime;
public sealed record AmbiguousLocalTime(ImmutableArray<TimeSpan> Offsets);
public readonly union LocalTimeFact(ValidLocalTime, SkippedLocalTime, AmbiguousLocalTime);
public sealed record LocalTimeFacts(ZoneFact Zone, LocalTimeFact LocalTime);
public sealed record Scheduled(DateTimeOffset UtcInstant);
public sealed record RequiresOffsetChoice(ImmutableArray<TimeSpan> CandidateOffsets);
public sealed record ScheduleRejected(string Code);
public readonly union ScheduleDecision(Scheduled, RequiresOffsetChoice, ScheduleRejected);

public static class SchedulingPolicy
{
    public static ScheduleDecision Decide(ScheduleRequest request, LocalTimeFacts facts) => facts.Zone switch
    {
        ZoneUnknown unknown => new ScheduleRejected($"unknown-zone:{unknown.Id}"),
        ZoneKnown => DecideKnown(request, facts.LocalTime)
    };

    public static string Canonical(ScheduleDecision decision) => decision switch
    {
        Scheduled scheduled => $"scheduled|{scheduled.UtcInstant:O}",
        RequiresOffsetChoice choice => $"needs-choice|{string.Join(',', choice.CandidateOffsets.Select(FormatOffset))}",
        ScheduleRejected rejected => $"rejected|{rejected.Code}"
    };

    private static ScheduleDecision DecideKnown(ScheduleRequest request, LocalTimeFact localTime) => localTime switch
    {
        ValidLocalTime valid => new Scheduled(new DateTimeOffset(request.Date.ToDateTime(request.Time), valid.Offset).ToUniversalTime()),
        SkippedLocalTime => new ScheduleRejected("nonexistent-local-time"),
        AmbiguousLocalTime ambiguous => new RequiresOffsetChoice(ambiguous.Offsets)
    };

    private static string FormatOffset(TimeSpan offset) => $"{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
}

public static class TimeZoneBoundary
{
    // This is an effect adapter. The decision policy receives its value and never queries host time-zone data.
    public static LocalTimeFacts Inspect(ScheduleRequest request)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(request.ZoneId);
            var local = request.Date.ToDateTime(request.Time, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) { return new LocalTimeFacts(new ZoneKnown(zone.Id), new SkippedLocalTime()); }
            if (zone.IsAmbiguousTime(local)) { return new LocalTimeFacts(new ZoneKnown(zone.Id), new AmbiguousLocalTime(zone.GetAmbiguousTimeOffsets(local).ToImmutableArray())); }
            return new LocalTimeFacts(new ZoneKnown(zone.Id), new ValidLocalTime(zone.GetUtcOffset(local)));
        }
        catch (TimeZoneNotFoundException)
        {
            return new LocalTimeFacts(new ZoneUnknown(request.ZoneId), new SkippedLocalTime());
        }
        catch (InvalidTimeZoneException)
        {
            return new LocalTimeFacts(new ZoneUnknown(request.ZoneId), new SkippedLocalTime());
        }
    }
}
