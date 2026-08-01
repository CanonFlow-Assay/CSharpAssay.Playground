namespace Playground.Gof.Refined;

public enum ApprovalState
{
    Draft,
    Submitted,
    Approved
}

public static class Approvals
{
    public static ApprovalState Submit(ApprovalState state) =>
        state == ApprovalState.Draft ? ApprovalState.Submitted : state;

    public static ApprovalState Approve(ApprovalState state) =>
        state == ApprovalState.Submitted ? ApprovalState.Approved : state;

    public static string Status(ApprovalState state) => state switch
    {
        ApprovalState.Draft => "draft",
        ApprovalState.Submitted => "submitted",
        ApprovalState.Approved => "approved",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
