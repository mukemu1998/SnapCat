using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
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
        _settings = TranslationLanguageHelper.CloneSettings(settings);
        _settings.NormalizeApiProfiles();
        _viewModel = new TranslationPopupViewModel(_app.TranslationService, _settings);
        DataContext = _viewModel;
        _captureRegion = captureRegion;
        _ownerWindow = ownerWindow;
        _repeatCaptureAction = repeatCaptureAction;

        Title = title;
        _viewModel.Reset(title, status, sourceText, translatedText, _settings, repeatCaptureAction);

        ConfigureApiProfiles();
        SetTranslationProvider(_settings.TranslationProviderPreference);
        Loaded += TranslationPopupWindow_OnLoaded;
    }

    public void PrepareForReuse(
        string title,
        AppSettings settings,
        Int32Rect? captureRegion,
        Func<Task>? repeatCaptureAction,
        bool preserveCurrentPosition)
    {
        _settings = TranslationLanguageHelper.CloneSettings(settings);
        _settings.NormalizeApiProfiles();
        _captureRegion = captureRegion;
        _repeatCaptureAction = repeatCaptureAction;
        _hasAnchoredPosition = preserveCurrentPosition;

        Title = title;
        _viewModel.Reset(title, "正在识别文本...", string.Empty, string.Empty, _settings, repeatCaptureAction);
        TranslatedTextBox.Height = double.NaN;

        ConfigureApiProfiles();
        SetTranslationProvider(_settings.TranslationProviderPreference);

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

    public void SetBusyState(string status)
    {
        _viewModel.SetBusyState(status);
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

}
