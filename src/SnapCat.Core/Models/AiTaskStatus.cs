namespace SnapCat.Core.Models;

public enum AiTaskStatus
{
    Draft,
    Ready,
    Queued,
    Running,
    WaitingForInput,
    Cancelling,
    Cancelled,
    Succeeded,
    Failed,
    Blocked,
    Interrupted
}
