using SnapCat.App.Windows;

namespace SnapCat.App.Services;

public sealed class PinnedWindowRegistryService
{
    private readonly HashSet<PinnedImageWindow> _windows = [];

    public void Register(PinnedImageWindow window)
    {
        if (_windows.Add(window))
        {
            window.Closed += Window_OnClosed;
        }
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

    public void CloseAllWindows()
    {
        foreach (var window in _windows.ToArray())
        {
            window.Close();
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
    }
}
