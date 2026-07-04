using System.IO;
using System.Windows.Threading;
using SnapCat.App.Windows;
using SnapCat.Core.Models;

namespace SnapCat.App.Services;

public sealed class PinnedWindowRegistryService
{
    private const int RestoreBatchSize = 4;

    public const string UngroupedGroupName = "";
    public const string GroupOneName = "贴图组 1";
    public const string GroupTwoName = "贴图组 2";
    public const string GroupThreeName = "贴图组 3";

    private readonly HashSet<PinnedImageWindow> _windows = [];
    private readonly PinnedWindowLayoutStore _layoutStore;
    private bool _isClosingForExit;

    public PinnedWindowRegistryService(PinnedWindowLayoutStore layoutStore)
    {
        _layoutStore = layoutStore;
    }

    public static IReadOnlyList<string> BuiltInGroups { get; } =
    [
        GroupOneName,
        GroupTwoName,
        GroupThreeName
    ];

    public void Register(PinnedImageWindow window)
    {
        if (_windows.Add(window))
        {
            window.Closed += Window_OnClosed;
        }
    }

    public async void RestorePersistedWindows(AppSettings settings)
    {
        var snapshots = _layoutStore.Load()
            .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.ImagePath) && File.Exists(snapshot.ImagePath))
            .ToList();

        if (snapshots.Count == 0)
        {
            _layoutStore.Save([]);
            return;
        }

        var restoredCount = 0;
        foreach (var snapshot in snapshots)
        {
            var window = new PinnedImageWindow(
                snapshot.ImagePath,
                TranslationLanguageHelper.CloneSettings(settings),
                persistedSnapshot: snapshot);
            window.Show();

            if (!snapshot.IsVisible)
            {
                window.Hide();
            }

            restoredCount++;
            if (restoredCount % RestoreBatchSize == 0)
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }

        SaveActiveWindows();
    }

    public void SaveActiveWindows()
    {
        _layoutStore.Save(_windows
            .Select(window => window.CreateSnapshot())
            .Where(static snapshot => snapshot.Width > 0 && snapshot.Height > 0));
    }

    public IReadOnlyList<PinnedWindowSnapshot> GetActiveSnapshots()
    {
        return _windows
            .Select(window => window.CreateSnapshot())
            .OrderBy(snapshot => snapshot.GroupName)
            .ThenBy(snapshot => snapshot.UpdatedAt)
            .ToList();
    }

    public IReadOnlyList<string> GetAvailableGroupNames()
    {
        return _windows
            .Select(window => window.GroupName)
            .Where(groupName => !string.IsNullOrWhiteSpace(groupName))
            .Concat(BuiltInGroups)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(groupName => groupName)
            .ToList();
    }

    public void SetWindowGroup(PinnedImageWindow window, string groupName)
    {
        window.GroupName = groupName.Trim();
        SaveActiveWindows();
    }

    public void ShowAllWindows()
    {
        foreach (var window in _windows)
        {
            window.Show();
            window.BringPinnedWindowToFront();
        }

        SaveActiveWindows();
    }

    public void ShowUngroupedWindows()
    {
        ShowGroup(UngroupedGroupName);
    }

    public void HideAllWindows()
    {
        foreach (var window in _windows)
        {
            window.Hide();
        }

        SaveActiveWindows();
    }

    public void ShowGroup(string groupName)
    {
        foreach (var window in _windows)
        {
            var shouldShow = string.Equals(window.GroupName, groupName, StringComparison.Ordinal);
            if (shouldShow)
            {
                window.Show();
                window.BringPinnedWindowToFront();
            }
            else
            {
                window.Hide();
            }
        }

        SaveActiveWindows();
    }

    public void CloseSnapshots(IEnumerable<string> ids)
    {
        var idSet = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var window in _windows.ToArray())
        {
            if (idSet.Contains(window.PinnedId))
            {
                window.Close();
            }
        }
    }

    public void SetSnapshotsGroup(IEnumerable<string> ids, string groupName)
    {
        var idSet = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var window in _windows)
        {
            if (idSet.Contains(window.PinnedId))
            {
                window.GroupName = groupName.Trim();
            }
        }

        SaveActiveWindows();
    }

    public void CloseOtherWindows(PinnedImageWindow currentWindow)
    {
        foreach (var window in _windows.ToArray())
        {
            if (!ReferenceEquals(window, currentWindow))
            {
                window.Close();
            }
        }
    }

    public bool HasOtherWindows(PinnedImageWindow currentWindow)
    {
        return _windows.Any(window => !ReferenceEquals(window, currentWindow));
    }

    public void CloseAllWindows(bool preserveState = false)
    {
        _isClosingForExit = preserveState;
        if (preserveState)
        {
            if (_windows.Count == 0)
            {
                _isClosingForExit = false;
                return;
            }

            SaveActiveWindows();
        }

        try
        {
            foreach (var window in _windows.ToArray())
            {
                window.Close();
            }
        }
        finally
        {
            _isClosingForExit = false;
        }

        if (!preserveState)
        {
            SaveActiveWindows();
        }
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        if (sender is not PinnedImageWindow window)
        {
            return;
        }

        window.Closed -= Window_OnClosed;
        _windows.Remove(window);

        if (!_isClosingForExit)
        {
            SaveActiveWindows();
        }
    }
}
