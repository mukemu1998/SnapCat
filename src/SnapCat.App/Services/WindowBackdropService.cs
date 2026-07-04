using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Forms;

namespace SnapCat.App.Services;

internal static class WindowBackdropService
{
    private const int DwmaUseImmersiveDarkMode = 20;
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmaSystemBackdropType = 38;
    private const int WcaAccentPolicy = 19;

    public static void ApplyToWindow(
        Window window,
        BackdropKind backdropKind,
        bool useSystemRoundedCorners = false,
        double clipCornerRadius = 0)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                PrepareWpfClientArea(window);
                ApplyToHandle(
                    handle,
                    backdropKind,
                    new BackdropColor(0xCC, 0x20, 0x20, 0x26),
                    useSystemRoundedCorners);

                if (clipCornerRadius > 0)
                {
                    ApplyRoundedRegion(window, clipCornerRadius);
                }
            }
        };

        if (clipCornerRadius > 0)
        {
            window.SizeChanged += (_, _) => ApplyRoundedRegion(window, clipCornerRadius);
        }
    }

    public static void ApplyToToolStrip(ToolStripDropDown dropDown)
    {
        dropDown.VisibleChanged += (_, _) =>
        {
            if (!dropDown.Visible)
            {
                return;
            }

            var handle = dropDown.Handle;
            if (handle != IntPtr.Zero)
            {
                ApplyToHandle(
                    handle,
                    BackdropKind.TransientWindow,
                    new BackdropColor(0xCC, 0x24, 0x24, 0x28),
                    useSystemRoundedCorners: true);
            }
        };
    }

    private static void PrepareWpfClientArea(Window window)
    {
        if (PresentationSource.FromVisual(window) is HwndSource source
            && source.CompositionTarget is not null)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }

        var helper = new WindowInteropHelper(window);
        var margins = new Margins(-1);
        _ = DwmExtendFrameIntoClientArea(helper.Handle, ref margins);
    }

    private static void ApplyRoundedRegion(Window window, double cornerRadius)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            return;
        }

        var source = PresentationSource.FromVisual(window);
        var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var sizePx = toDevice.Transform(new System.Windows.Point(window.ActualWidth, window.ActualHeight));
        var radiusPx = toDevice.Transform(new System.Windows.Point(cornerRadius, cornerRadius));
        var width = Math.Max(1, (int)Math.Round(sizePx.X));
        var height = Math.Max(1, (int)Math.Round(sizePx.Y));
        var ellipseWidth = Math.Max(2, (int)Math.Round(radiusPx.X * 2));
        var ellipseHeight = Math.Max(2, (int)Math.Round(radiusPx.Y * 2));

        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, ellipseWidth, ellipseHeight);
        SetWindowRgn(handle, region, true);
    }

    private static void ApplyToHandle(
        IntPtr handle,
        BackdropKind backdropKind,
        BackdropColor accentColor,
        bool useSystemRoundedCorners)
    {
        TrySetDarkMode(handle);
        TrySetCornerPreference(handle, useSystemRoundedCorners);

        if (!TrySetSystemBackdrop(handle, backdropKind))
        {
            TrySetAccentBlur(handle, accentColor);
        }
    }

    private static void TrySetDarkMode(IntPtr handle)
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, DwmaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    private static void TrySetCornerPreference(IntPtr handle, bool useSystemRoundedCorners)
    {
        var cornerPreference = (int)(useSystemRoundedCorners
            ? DwmWindowCornerPreference.Round
            : DwmWindowCornerPreference.DoNotRound);
        _ = DwmSetWindowAttribute(handle, DwmaWindowCornerPreference, ref cornerPreference, sizeof(int));
    }

    private static bool TrySetSystemBackdrop(IntPtr handle, BackdropKind backdropKind)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return false;
        }

        var attributeValue = (int)backdropKind;
        return DwmSetWindowAttribute(handle, DwmaSystemBackdropType, ref attributeValue, sizeof(int)) == 0;
    }

    private static void TrySetAccentBlur(IntPtr handle, BackdropColor accentColor)
    {
        var accentPolicy = new AccentPolicy
        {
            AccentState = AccentState.EnableAcrylicBlurBehind,
            AccentFlags = 0,
            GradientColor = accentColor.ToAbgr(),
            AnimationId = 0
        };

        var accentPolicySize = Marshal.SizeOf<AccentPolicy>();
        var accentPtr = Marshal.AllocHGlobal(accentPolicySize);

        try
        {
            Marshal.StructureToPtr(accentPolicy, accentPtr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                SizeOfData = accentPolicySize,
                Data = accentPtr
            };

            _ = SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    private readonly record struct BackdropColor(byte A, byte R, byte G, byte B)
    {
        public int ToAbgr() => (A << 24) | (B << 16) | (G << 8) | R;
    }

    public enum BackdropKind
    {
        Auto = 0,
        None = 1,
        MainWindow = 2,
        TransientWindow = 3,
        TabbedWindow = 4
    }

    private enum AccentState
    {
        Disabled = 0,
        EnableGradient = 1,
        EnableTransparentGradient = 2,
        EnableBlurBehind = 3,
        EnableAcrylicBlurBehind = 4
    }

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public Margins(int uniformMargin)
        {
            Left = uniformMargin;
            Right = uniformMargin;
            Top = uniformMargin;
            Bottom = uniformMargin;
        }

        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd,
        ref WindowCompositionAttributeData data);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int widthEllipse,
        int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(
        IntPtr hWnd,
        IntPtr hRgn,
        bool bRedraw);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(
        IntPtr hwnd,
        ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
