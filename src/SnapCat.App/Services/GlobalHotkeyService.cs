using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SnapCat.Core.Models;
using WpfApplication = System.Windows.Application;

namespace SnapCat.App.Services;

public sealed record HotkeyRegistrationResult(
    string Label,
    string HotkeyText,
    bool IsRegistered,
    string Message);

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly Dictionary<int, Action> _handlers = [];
    private readonly string _logFilePath;
    private HwndSource? _hwndSource;
    private int _nextId = 1;

    public GlobalHotkeyService()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SnapCat",
            "logs");
        Directory.CreateDirectory(logDirectory);
        _logFilePath = Path.Combine(logDirectory, "hotkey.log");
    }

    public string LogFilePath => _logFilePath;

    public void Attach(Window window)
    {
        var helper = new WindowInteropHelper(window);
        var handle = helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(handle);

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.AddHook(WndProc);
        }

        AppendLog($"attach hwnd=0x{handle.ToInt64():X} source={(_hwndSource is null ? "null" : "ok")}");
    }

    public IReadOnlyList<HotkeyRegistrationResult> RegisterAll(
        AppSettings settings,
        Action pinAction,
        Action translateAction,
        Action waitAction)
    {
        UnregisterAll();

        return
        [
            Register("固定到屏幕", settings.HotkeyCaptureAndPin, pinAction),
            Register("自动翻译", settings.HotkeyCaptureAndTranslate, translateAction),
            Register("等待操作", settings.HotkeyCaptureAndWaitForAction, waitAction)
        ];
    }

    public void UnregisterAll()
    {
        if (_hwndSource is null)
        {
            _handlers.Clear();
            AppendLog("unregister_all skipped: hwndSource null");
            return;
        }

        foreach (var id in _handlers.Keys.ToArray())
        {
            UnregisterHotKey(_hwndSource.Handle, id);
            AppendLog($"unregister id={id}");
        }

        _handlers.Clear();
        _nextId = 1;
    }

    public void Dispose()
    {
        UnregisterAll();

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    private HotkeyRegistrationResult Register(string label, string hotkeyText, Action action)
    {
        if (_hwndSource is null)
        {
            AppendLog($"register failed label={label} hotkey={hotkeyText} reason=no_hwnd_source");
            return new HotkeyRegistrationResult(label, hotkeyText, false, "窗口句柄尚未准备好");
        }

        if (string.IsNullOrWhiteSpace(hotkeyText))
        {
            AppendLog($"register skipped label={label} reason=empty_hotkey");
            return new HotkeyRegistrationResult(label, hotkeyText, false, "未设置快捷键");
        }

        if (!TryParseHotkey(hotkeyText, out var modifiers, out var key))
        {
            AppendLog($"register failed label={label} hotkey={hotkeyText} reason=parse_failed");
            return new HotkeyRegistrationResult(label, hotkeyText, false, "快捷键格式无法识别");
        }

        var id = _nextId++;
        if (RegisterHotKey(_hwndSource.Handle, id, modifiers, key))
        {
            _handlers[id] = action;
            AppendLog($"register ok label={label} hotkey={hotkeyText} id={id} modifiers=0x{modifiers:X} key=0x{key:X}");
            return new HotkeyRegistrationResult(label, hotkeyText, true, "注册成功");
        }

        var errorCode = Marshal.GetLastWin32Error();
        AppendLog($"register failed label={label} hotkey={hotkeyText} id={id} error={errorCode}");
        return new HotkeyRegistrationResult(
            label,
            hotkeyText,
            false,
            GetRegisterFailureMessage(errorCode));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            var id = wParam.ToInt32();
            AppendLog($"wm_hotkey id={id} known={_handlers.ContainsKey(id)}");

            if (_handlers.TryGetValue(id, out var handler))
            {
                handled = true;
                WpfApplication.Current.Dispatcher.BeginInvoke(handler);
            }
        }

        return IntPtr.Zero;
    }

    private static bool TryParseHotkey(string text, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            var isLast = index == parts.Length - 1;

            if (!isLast)
            {
                var modifier = part.ToLowerInvariant() switch
                {
                    "ctrl" or "control" => 0x0002u,
                    "alt" => 0x0001u,
                    "shift" => 0x0004u,
                    "win" or "windows" => 0x0008u,
                    _ => uint.MaxValue
                };

                if (modifier == uint.MaxValue)
                {
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
            {
                key = char.ToUpperInvariant(part[0]);
                return true;
            }

            if (Enum.TryParse<Key>(part, true, out var wpfKey))
            {
                key = (uint)KeyInterop.VirtualKeyFromKey(wpfKey);
                return key != 0;
            }

            return false;
        }

        return false;
    }

    private static string GetRegisterFailureMessage(int errorCode)
    {
        return errorCode switch
        {
            1409 => "已被其他程序占用",
            87 => "参数无效，请重新录制快捷键",
            _ => $"注册失败，错误码 {errorCode}"
        };
    }

    private void AppendLog(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
        File.AppendAllText(_logFilePath, line, Encoding.UTF8);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
