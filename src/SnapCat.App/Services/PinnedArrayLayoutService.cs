namespace SnapCat.App.Services;

internal static class PinnedArrayLayoutService
{
    public static PinnedArrayLayout Calculate(
        PinnedArrayDirection direction,
        double sourceLeft,
        double sourceTop,
        double sourceWidth,
        double sourceHeight,
        int tileCount)
    {
        var isHorizontal = direction is PinnedArrayDirection.Left or PinnedArrayDirection.Right;
        var targetWidth = isHorizontal ? sourceWidth * tileCount : sourceWidth;
        var targetHeight = isHorizontal ? sourceHeight : sourceHeight * tileCount;
        var targetLeft = sourceLeft;
        var targetTop = sourceTop;

        if (direction == PinnedArrayDirection.Right)
        {
            targetLeft += sourceWidth;
        }
        else if (direction == PinnedArrayDirection.Left)
        {
            targetLeft -= targetWidth;
        }
        else if (direction == PinnedArrayDirection.Down)
        {
            targetTop += sourceHeight;
        }
        else if (direction == PinnedArrayDirection.Up)
        {
            targetTop -= targetHeight;
        }

        return new PinnedArrayLayout(targetLeft, targetTop, targetWidth, targetHeight);
    }
}
