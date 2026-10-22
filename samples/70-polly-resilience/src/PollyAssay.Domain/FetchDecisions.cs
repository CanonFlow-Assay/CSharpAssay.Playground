namespace PollyAssay.Domain;

public enum ReplyKind { Success, Transient, Permanent }
public sealed record TransportReply(ReplyKind Kind, string Content);
public sealed record Fetched(string Content);
public enum FetchFailureKind { Permanent, Exhausted, Deadline, Rejected }
public sealed record FetchFailed(FetchFailureKind Kind);
public readonly record struct FetchCancelled;
public sealed record FetchDegraded(string Content);
public readonly union FetchOutcome(Fetched, FetchFailed, FetchCancelled, FetchDegraded);
public sealed record FetchResponse(string Status, string Content);

public static class FetchDecisions
{
    public static bool ShouldRetry(TransportReply reply) => reply.Kind == ReplyKind.Transient;
    public static FetchOutcome Convert(TransportReply reply) => reply.Kind switch
    {
        ReplyKind.Success => new Fetched(reply.Content),
        ReplyKind.Permanent => new FetchFailed(FetchFailureKind.Permanent),
        ReplyKind.Transient => new FetchFailed(FetchFailureKind.Exhausted),
        _ => throw new ArgumentOutOfRangeException(nameof(reply))
    };
    public static FetchResponse Project(FetchOutcome outcome) => outcome switch
    {
        Fetched value => new("success", value.Content),
        FetchDegraded value => new("degraded", value.Content),
        FetchCancelled => new("cancelled", ""),
        FetchFailed value => new(value.Kind switch
        {
            FetchFailureKind.Permanent => "permanent",
            FetchFailureKind.Exhausted => "exhausted",
            FetchFailureKind.Deadline => "timeout",
            FetchFailureKind.Rejected => "rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        }, "")
    };
}
