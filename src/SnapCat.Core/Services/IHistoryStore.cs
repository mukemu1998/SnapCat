using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

public interface IHistoryStore
{
    Task<IReadOnlyList<CaptureTranslationRecord>> LoadRecentAsync(int count, CancellationToken cancellationToken = default);

    Task AppendAsync(CaptureTranslationRecord record, CancellationToken cancellationToken = default);

    Task DeleteAsync(CaptureTranslationRecord record, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
