using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;

namespace SnapCat.App.Services;

public static class WindowsTextExtractorLauncher
{
    private static readonly TimeSpan TextExtractorReadyDelay = TimeSpan.FromMilliseconds(1100);
    private static readonly TimeSpan CopyRetryDelay = TimeSpan.FromMilliseconds(850);
    private const int CopyAttemptCount = 3;
    private const ushort VirtualKeyLeftWindows = 0x5B;
    private const ushort VirtualKeyShift = 0x10;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyA = 0x41;
    private const ushort VirtualKeyC = 0x43;
    private const ushort VirtualKeyT = 0x54;
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    public static async Task LaunchTextExtractorShortcutAsync(
        Int32Rect? autoSelectRegion = null,
        CancellationToken cancellationToken = default)
    {
        SendKeyboardShortcut();

        if (autoSelectRegion is null || autoSelectRegion.Value.Width < 2 || autoSelectRegion.Value.Height < 2)
        {
            return;
        }

        await Task.Delay(900, cancellationToken);
        AutoDragSelection(autoSelectRegion.Value);
        await SendDelayedSelectAllAndCopyAsync(cancellationToken);
    }

    private static void SendKeyboardShortcut()
    {
        var inputs = new[]
        {
            CreateKeyboardInput(VirtualKeyLeftWindows, 0),
            CreateKeyboardInput(VirtualKeyShift, 0),
            CreateKeyboardInput(VirtualKeyT, 0),
            CreateKeyboardInput(VirtualKeyT, KeyEventKeyUp),
            CreateKeyboardInput(VirtualKeyShift, KeyEventKeyUp),
            CreateKeyboardInput(VirtualKeyLeftWindows, KeyEventKeyUp)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "发送 Win+Shift+T 快捷键失败。");
        }
    }

    private static async Task SendDelayedSelectAllAndCopyAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TextExtractorReadyDelay, cancellationToken);

        for (var attempt = 0; attempt < CopyAttemptCount; attempt++)
        {
            SendSelectAllAndCopyShortcut();
            if (attempt < CopyAttemptCount - 1)
            {
                await Task.Delay(CopyRetryDelay, cancellationToken);
            }
        }
    }

    private static void SendSelectAllAndCopyShortcut()
    {
        SendKeyboardChord(VirtualKeyControl, VirtualKeyA, "发送文本提取全选快捷键失败。");
        Thread.Sleep(120);
        SendKeyboardChord(VirtualKeyControl, VirtualKeyC, "发送文本提取复制快捷键失败。");
    }

    private static void SendKeyboardChord(ushort modifierKey, ushort actionKey, string errorMessage)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(modifierKey, 0),
            CreateKeyboardInput(actionKey, 0),
            CreateKeyboardInput(actionKey, KeyEventKeyUp),
            CreateKeyboardInput(modifierKey, KeyEventKeyUp)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), errorMessage);
        }
    }

    private static void AutoDragSelection(Int32Rect region)
    {
        var inset = Math.Min(8, Math.Max(1, Math.Min(region.Width, region.Height) / 6));
        var startX = region.X + inset;
        var startY = region.Y + inset;
        var endX = region.X + Math.Max(inset + 1, region.Width - inset);
        var endY = region.Y + Math.Max(inset + 1, region.Height - inset);

        if (!SetCursorPos(startX, startY))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "移动鼠标到文本提取起点失败。");
        }

        SendMouseClick(MouseEventLeftDown);
        Thread.Sleep(80);

        if (!SetCursorPos(endX, endY))
        {
            SendMouseClick(MouseEventLeftUp);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "移动鼠标到文本提取终点失败。");
        }

        Thread.Sleep(80);
        SendMouseClick(MouseEventLeftUp);
    }

    private static void SendMouseClick(uint flags)
    {
        var inputs = new[] { CreateMouseInput(flags) };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "发送文本提取鼠标拖选事件失败。");
        }
    }

    private static Input CreateKeyboardInput(ushort virtualKey, uint flags)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = flags
                }
            }
        };
    }

    private static Input CreateMouseInput(uint flags)
    {
        return new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Flags = flags
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInputStructure);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
