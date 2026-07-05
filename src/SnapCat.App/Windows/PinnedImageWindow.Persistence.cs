using SnapCat.App.Services;
using WpfPoint = System.Windows.Point;

namespace SnapCat.App.Windows;

public partial class PinnedImageWindow
{
    private void ImportViewState(double left, double top, double scale)
    {
        Left = left;
        Top = top;
        ApplyScale(scale, new WpfPoint(0, 0));
    }

    private void ImportDisplayedBounds(double left, double top, double width, double height)
    {
        Left = left;
        Top = top;
        Width = Math.Max(1d, width);
        Height = Math.Max(1d, height);

        if (_originalWidth > 0 && _originalHeight > 0)
        {
            var scaleX = Width / _originalWidth;
            var scaleY = Height / _originalHeight;
            _currentScale = Math.Min(scaleX, scaleY);
        }
    }

    public PinnedWindowSnapshot CreateSnapshot()
    {
        var safeWidth = NormalizeSnapshotNumber(Width, _originalWidth);
        var safeHeight = NormalizeSnapshotNumber(Height, _originalHeight);
        return new PinnedWindowSnapshot
        {
            Id = PinnedId,
            ImagePath = _imagePath,
            GroupName = GroupName.Trim(),
            IsVisible = IsVisible,
            Left = NormalizeSnapshotPosition(Left),
            Top = NormalizeSnapshotPosition(Top),
            Width = safeWidth,
            Height = safeHeight,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private static double NormalizeSnapshotNumber(double value, double fallback)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return double.IsFinite(fallback) && fallback > 0 ? fallback : 1d;
        }

        return value;
    }

    private static double NormalizeSnapshotPosition(double value)
    {
        return double.IsFinite(value) ? value : 0d;
    }
}
