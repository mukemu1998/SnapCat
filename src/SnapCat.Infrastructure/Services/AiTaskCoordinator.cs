using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

public sealed class AiTaskCoordinator : IAiTaskCoordinator
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, AiTaskRun> _tasks = new(StringComparer.Ordinal);

    public AiTaskRun Create(AiTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var task = new AiTaskRun
        {
            Kind = request.Kind,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? GetDefaultDisplayName(request.Kind)
                : request.DisplayName.Trim(),
            ProviderProfileId = request.ProviderProfileId?.Trim() ?? string.Empty,
            ModelName = request.ModelName?.Trim() ?? string.Empty,
            ReferenceImageCount = Math.Max(0, request.ReferenceImageCount),
            OutputCount = Math.Clamp(request.OutputCount, 1, 16),
            Status = AiTaskStatus.Ready
        };

        lock (_syncRoot)
        {
            _tasks[task.Id] = task;
            return task.Clone();
        }
    }

    public AiTaskRun? Get(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        lock (_syncRoot)
        {
            return _tasks.TryGetValue(taskId, out var task) ? task.Clone() : null;
        }
    }

    public IReadOnlyList<AiTaskRun> GetAll()
    {
        lock (_syncRoot)
        {
            return _tasks.Values
                .OrderByDescending(task => task.CreatedAt)
                .Select(task => task.Clone())
                .ToList();
        }
    }

    public bool TryTransition(string taskId, AiTaskStatus nextStatus, string? errorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return false;
        }

        lock (_syncRoot)
        {
            if (!_tasks.TryGetValue(taskId, out var task) || !CanTransition(task.Status, nextStatus))
            {
                return false;
            }

            task.Status = nextStatus;
            task.ErrorMessage = nextStatus is AiTaskStatus.Failed or AiTaskStatus.Blocked or AiTaskStatus.Interrupted
                ? errorMessage?.Trim() ?? string.Empty
                : string.Empty;
            task.UpdatedAt = DateTimeOffset.Now;
            return true;
        }
    }

    public int InterruptActiveTasks(string reason)
    {
        lock (_syncRoot)
        {
            var interruptedCount = 0;
            foreach (var task in _tasks.Values.Where(task => IsActive(task.Status)))
            {
                task.Status = AiTaskStatus.Interrupted;
                task.ErrorMessage = string.IsNullOrWhiteSpace(reason) ? "应用已退出。" : reason.Trim();
                task.UpdatedAt = DateTimeOffset.Now;
                interruptedCount++;
            }

            return interruptedCount;
        }
    }

    private static bool CanTransition(AiTaskStatus currentStatus, AiTaskStatus nextStatus)
    {
        if (currentStatus == nextStatus)
        {
            return true;
        }

        return currentStatus switch
        {
            AiTaskStatus.Draft => nextStatus is AiTaskStatus.Ready or AiTaskStatus.Cancelled,
            AiTaskStatus.Ready => nextStatus is AiTaskStatus.Queued or AiTaskStatus.Blocked or AiTaskStatus.Cancelled,
            AiTaskStatus.Queued => nextStatus is AiTaskStatus.Running or AiTaskStatus.Blocked or AiTaskStatus.Failed or AiTaskStatus.Cancelled or AiTaskStatus.Interrupted,
            AiTaskStatus.Running => nextStatus is AiTaskStatus.Succeeded or AiTaskStatus.Failed or AiTaskStatus.WaitingForInput or AiTaskStatus.Cancelling or AiTaskStatus.Interrupted,
            AiTaskStatus.WaitingForInput => nextStatus is AiTaskStatus.Queued or AiTaskStatus.Failed or AiTaskStatus.Cancelled or AiTaskStatus.Interrupted,
            AiTaskStatus.Cancelling => nextStatus is AiTaskStatus.Cancelled or AiTaskStatus.Failed or AiTaskStatus.Interrupted,
            AiTaskStatus.Failed => nextStatus is AiTaskStatus.Queued or AiTaskStatus.Cancelled,
            AiTaskStatus.Blocked => nextStatus is AiTaskStatus.Ready or AiTaskStatus.Cancelled,
            _ => false
        };
    }

    private static bool IsActive(AiTaskStatus status)
    {
        return status is AiTaskStatus.Ready
            or AiTaskStatus.Queued
            or AiTaskStatus.Running
            or AiTaskStatus.WaitingForInput
            or AiTaskStatus.Cancelling
            or AiTaskStatus.Blocked;
    }

    private static string GetDefaultDisplayName(AiTaskKind kind)
    {
        return kind switch
        {
            AiTaskKind.VisionAnalysis => "视觉分析",
            AiTaskKind.PromptRefinement => "提示词整理",
            AiTaskKind.ImageGeneration => "图片生成",
            AiTaskKind.ImageEditing => "图片编辑",
            AiTaskKind.ModelDownload => "模型下载",
            AiTaskKind.WorkflowRun => "工作流运行",
            _ => "AI 任务"
        };
    }
}
