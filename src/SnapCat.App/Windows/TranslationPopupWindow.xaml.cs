using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using SnapCat.App.Services;
using SnapCat.App.ViewModels;
using SnapCat.Core.Models;
using Clipboard = System.Windows.Clipboard;
using FormsScreen = System.Windows.Forms.Screen;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Windows;

public partial class TranslationPopupWindow : Window
{
    private const double PopupGap = 8;
    private const double PopupVerticalMargin = 24;
    private const double MinimumTranslatedTextBoxHeight = 100;
    private const double TranslatedTextBoxPaddingAllowance = 28;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private readonly App _app;
    private Int32Rect? _captureRegion;
    private readonly Window? _ownerWindow;
    private AppSettings _settings;
    private readonly TranslationPopupViewModel _viewModel;
    private Func<Task>? _repeatCaptureAction;
    private bool _hasAnchoredPosition;
    private bool _isApplyingApiProfileSelection;
    public TranslationPopupWindow(
        string title,
        string status,
        string sourceText,
        string translatedText,
        AppSettings settings,
        Int32Rect? captureRegion = null,
        Window? ownerWindow = null,
        Func<Task>? repeatCaptureAction = null)
    {
        InitializeComponent();
        _app = (App)WpfApplication.Current;
        _settings = TranslationPopupSessionSettingsService.CreateInitialSettings(settings);
        _viewModel = new TranslationPopupViewModel(
            _app.TranslationService,
            _app.TranslationSpeechService,
            _settings);
        DataContext = _viewModel;
        _captureRegion = captureRegion;
        _ownerWindow = ownerWindow;
        _repeatCaptureAction = repeatCaptureAction;

        Title = title;
        _viewModel.Reset(title, status, sourceText, translatedText, _settings, repeatCaptureAction);

        ConfigureApiProfiles();
        SetTranslationProvider(_settings.TranslationProviderPreference);
        Loaded += TranslationPopupWindow_OnLoaded;
        ApplyOcrTooltipText();
    }

    public void PrepareForReuse(
        string title,
        AppSettings settings,
        Int32Rect? captureRegion,
        Func<Task>? repeatCaptureAction,
        bool preserveCurrentPosition)
    {
        _settings = TranslationPopupSessionSettingsService.CreateReuseSettings(settings, _settings);

        _captureRegion = captureRegion;
        _repeatCaptureAction = repeatCaptureAction;
        _hasAnchoredPosition = preserveCurrentPosition;

        Title = title;
        _viewModel.Reset(title, "正在识别文本...", string.Empty, string.Empty, _settings, repeatCaptureAction);
        TranslatedTextBox.Height = double.NaN;

        ConfigureApiProfiles();
        SetTranslationProvider(_settings.TranslationProviderPreference);
        ApplyOcrTooltipText();

        if (IsLoaded)
        {
            ApplyWindowHeightConstraints();
            AdjustTranslatedTextBoxHeight();
            if (!preserveCurrentPosition)
            {
                PositionNearAnchor();
            }
        }
    }

    public AppSettings CreateCurrentSettingsSnapshot()
    {
        return TranslationPopupSessionSettingsService.CreateExecutionSnapshot(_settings);
    }

    private void ApplyOcrTooltipText()
    {
        RecaptureButton.ToolTip = IsWindowsTextRecognitionEngine(_settings.OcrEngine)
            ? "再次框选 OCR 识别并自动复制"
            : "再次框选识别";
    }

    private static bool IsWindowsTextRecognitionEngine(string? value)
    {
        return string.Equals(value, "windows-text-extractor", StringComparison.Ordinal)
            || string.Equals(value, "windows-snipping-clipboard", StringComparison.Ordinal);
    }

    public void SetBusyState(string status)
    {
        _viewModel.SetBusyState(status);
    }

    public void ShowAboveSelectionOverlay()
    {
        if (!IsVisible)
        {
            Show();
        }

        ReassertTopmostWithoutActivation();
        Dispatcher.BeginInvoke(ReassertTopmostWithoutActivation, DispatcherPriority.ContextIdle);
    }

    public void UpdateRecognizedSource(string sourceText, string status)
    {
        _viewModel.UpdateRecognizedSource(sourceText, status);
    }

    public void UpdateTranslationResult(string translatedText, string status)
    {
        _viewModel.UpdateTranslationResult(translatedText, status);
        AdjustTranslatedTextBoxHeight();
    }

    public void UpdateFailure(string status, string? translatedText = null)
    {
        if (!string.IsNullOrWhiteSpace(translatedText))
        {
            _viewModel.TranslatedText = translatedText;
        }

        _viewModel.UpdateFailure(status);
        AdjustTranslatedTextBoxHeight();
    }

    private void TranslationPopupWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowHeightConstraints();
        AdjustTranslatedTextBoxHeight();
        PositionWindowIfNeeded();
        Activate();
    }

    private void ReassertTopmostWithoutActivation()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            Topmost = false;
            Topmost = true;
            return;
        }

        SetWindowPos(
            handle,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

}
