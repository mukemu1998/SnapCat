using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

public interface IAiTaskCoordinator
{
    AiTaskRun Create(AiTaskRequest request);

    AiTaskRun? Get(string taskId);

    IReadOnlyList<AiTaskRun> GetAll();

    bool TryTransition(string taskId, AiTaskStatus nextStatus, string? errorMessage = null);

    int InterruptActiveTasks(string reason);
}
