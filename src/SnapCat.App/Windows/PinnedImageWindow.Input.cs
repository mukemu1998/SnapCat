using System.Windows;
using System.Windows.Controls.Primitives;
using Keyboard = System.Windows.Input.Keyboard;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using WpfPoint = System.Windows.Point;

namespace SnapCat.App.Windows;

public partial class PinnedImageWindow
{
    private void Border_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInteractiveOverlayElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Focus();

        if (e.ClickCount >= 2)
        {
            ResetToOriginalScale(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        DragMove();
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (IsShortcutMatch(e, _settings.PinnedCloseShortcut))
        {
            Close();
            e.Handled = true;
            return;
        }

        if (IsShortcutMatch(e, _settings.PinnedHideShortcut))
        {
            HideCurrentPinnedWindow();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            Close();
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.C)
            {
                CopyDisplayedPinnedImageToClipboard();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.V)
            {
                PasteClipboardImageAsPinnedWindow();
                e.Handled = true;
                return;
            }

            if (IsZoomInKey(e.Key))
            {
                ApplyScaleDelta(ScaleStep);
                e.Handled = true;
                return;
            }

            if (IsZoomOutKey(e.Key))
            {
                ApplyScaleDelta(-ScaleStep);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                ResetToOriginalScale(GetWindowCenter());
                e.Handled = true;
                return;
            }
        }

        if (TryHandleArrowNudge(e.Key))
        {
            e.Handled = true;
        }
    }

    private static bool IsShortcutMatch(KeyEventArgs e, string? shortcutText)
    {
        if (string.IsNullOrWhiteSpace(shortcutText))
        {
            return false;
        }

        var parts = shortcutText.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var expectedModifiers = ModifierKeys.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            expectedModifiers |= parts[index].ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };
        }

        var expectedKey = ResolveShortcutKey(parts[^1]);
        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        return expectedKey != Key.None
            && expectedKey == actualKey
            && Keyboard.Modifiers == expectedModifiers;
    }

    private static Key ResolveShortcutKey(string keyText)
    {
        if (string.Equals(keyText, "Esc", StringComparison.OrdinalIgnoreCase))
        {
            return Key.Escape;
        }

        if (keyText.Length == 1 && char.IsDigit(keyText[0]))
        {
            return Key.D0 + (keyText[0] - '0');
        }

        return Enum.TryParse<Key>(keyText, true, out var key) ? key : Key.None;
    }

    private void Window_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PinnedContextMenu.IsOpen)
        {
            return;
        }

        var deltaStep = Math.Sign(e.Delta) * ScaleStep;
        ApplyScaleDelta(deltaStep, e.GetPosition(this));
        e.Handled = true;
    }

    private void ScaleThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        Focus();
    }

    private void ScaleThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var dominantLength = Math.Max(_originalWidth, _originalHeight);
        if (dominantLength <= 0)
        {
            return;
        }

        var delta = (e.HorizontalChange + e.VerticalChange) / dominantLength;
        ApplyScale(_currentScale + delta, new WpfPoint(0, 0));
    }

    private bool TryHandleArrowNudge(Key key)
    {
        var step = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control ? 10d : 1d;

        switch (key)
        {
            case Key.Left:
                Left -= step;
                return true;
            case Key.Right:
                Left += step;
                return true;
            case Key.Up:
                Top -= step;
                return true;
            case Key.Down:
                Top += step;
                return true;
            default:
                return false;
        }
    }

    private static bool IsZoomInKey(Key key)
        => key is Key.OemPlus or Key.Add;

    private static bool IsZoomOutKey(Key key)
        => key is Key.OemMinus or Key.Subtract;
}
