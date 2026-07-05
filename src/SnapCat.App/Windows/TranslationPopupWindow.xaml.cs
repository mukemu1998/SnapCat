using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SnapCat.App.Services;
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
        _captureRegion = captureRegion;
        _ownerWindow = ownerWindow;
        _repeatCaptureAction = repeatCaptureAction;

        Title = title;
        TitleTextBlock.Text = title;
        StatusTextBlock.Text = status;
        SourceTextBox.Text = sourceText;
        TranslatedTextBox.Text = translatedText;

        ConfigureLanguageChoices(sourceText);
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
        TitleTextBlock.Text = title;
        StatusTextBlock.Text = "正在识别文本...";
        SourceTextBox.Text = string.Empty;
        TranslatedTextBox.Text = string.Empty;
        TranslatedTextBox.Height = double.NaN;

        ConfigureLanguageChoices(string.Empty);
        ConfigureApiProfiles();
        SetTranslationProvider(_settings.TranslationProviderPreference);
        RecaptureButton.IsEnabled = _repeatCaptureAction is not null;

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
        StatusTextBlock.Text = status;
        TranslateButton.IsEnabled = false;
        RecaptureButton.IsEnabled = false;
    }

    public void UpdateRecognizedSource(string sourceText, string status)
    {
        SourceTextBox.Text = sourceText;
        StatusTextBlock.Text = status;
        UpdateDirectionHint();
    }

    public void UpdateTranslationResult(string translatedText, string status)
    {
        TranslatedTextBox.Text = translatedText;
        StatusTextBlock.Text = status;
        TranslateButton.IsEnabled = true;
        RecaptureButton.IsEnabled = _repeatCaptureAction is not null;
        AdjustTranslatedTextBoxHeight();
    }

    public void UpdateFailure(string status, string? translatedText = null)
    {
        if (!string.IsNullOrWhiteSpace(translatedText))
        {
            TranslatedTextBox.Text = translatedText;
        }

        StatusTextBlock.Text = status;
        TranslateButton.IsEnabled = true;
        RecaptureButton.IsEnabled = _repeatCaptureAction is not null;
        AdjustTranslatedTextBoxHeight();
    }

    private void TranslationPopupWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowHeightConstraints();
        AdjustTranslatedTextBoxHeight();
        PositionWindowIfNeeded();
        RecaptureButton.IsEnabled = _repeatCaptureAction is not null;
        Activate();
    }

}
