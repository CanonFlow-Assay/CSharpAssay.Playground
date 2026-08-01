namespace Playground.Gof.Classic;

public sealed class ApprovalWorkflow
{
    public bool IsSubmitted { get; private set; }

    public bool IsApproved { get; private set; }

    public void Submit() => IsSubmitted = true;

    public void Approve()
    {
        if (!IsSubmitted)
        {
            return;
        }

        IsApproved = true;
    }

    public string Status() =>
        IsApproved ? "approved" : IsSubmitted ? "submitted" : "draft";
}
