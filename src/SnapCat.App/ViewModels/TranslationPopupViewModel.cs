using System.Diagnostics;
using System.Windows;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using SnapCat.Core.Services;
using Clipboard = System.Windows.Clipboard;

namespace SnapCat.App.ViewModels;

public sealed class TranslationPopupViewModel : ObservableObject
{
    private readonly ITranslationService _translationService;
    private readonly TranslationSpeechService _speechService;

    private string _title = "翻译结果";
    private string _status = string.Empty;
    private string _sourceText = string.Empty;
    private string _translatedText = string.Empty;
    private string _sourceLanguageCode = TranslationLanguageHelper.AutoLanguage;
    private string _targetLanguageCode = TranslationLanguageHelper.AutoLanguage;
    private string _directionHint = "自动 -> 简体中文";
    private bool _isBusy;
    private bool _canRecapture;
    private Func<Task>? _repeatCaptureAction;

    public TranslationPopupViewModel(
        ITranslationService translationService,
        TranslationSpeechService speechService,
        AppSettings settings)
    {
        _translationService = translationService;
        _speechService = speechService;
        Settings = settings;
        TranslateCommand = new AsyncRelayCommand(TranslateAsync, CanTranslate);
        RecaptureCommand = new AsyncRelayCommand(RecaptureAsync, () => CanRecapture && !IsBusy);
        CopySourceCommand = new RelayCommand(CopySource, () => !string.IsNullOrEmpty(SourceText));
        CopyTranslatedCommand = new RelayCommand(CopyTranslated, () => !string.IsNullOrEmpty(TranslatedText));
        SpeakSourceCommand = new RelayCommand(SpeakSource, () => !string.IsNullOrWhiteSpace(SourceText));
        SpeakTranslatedCommand = new RelayCommand(SpeakTranslated, () => !string.IsNullOrWhiteSpace(TranslatedText));
        ClearSourceCommand = new RelayCommand(ClearSource, () => !string.IsNullOrWhiteSpace(SourceText));
    }

    public AppSettings Settings { get; private set; }

    public string SelectedProviderLabel { get; set; } = "本地翻译";

    public IReadOnlyList<TranslationLanguageDefinition> Languages => TranslationLanguageHelper.SupportedLanguages;

    public IReadOnlyList<ApiTranslationProfile> ApiProfiles => Settings.ApiProfiles;

    public string SelectedApiProfileId
    {
        get => Settings.SelectedApiProfileId;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(Settings.SelectedApiProfileId, normalized, StringComparison.Ordinal))
            {
                return;
            }

            Settings.SelectedApiProfileId = normalized;
            OnPropertyChanged();
        }
    }

    public AsyncRelayCommand TranslateCommand { get; }

    public AsyncRelayCommand RecaptureCommand { get; }

    public RelayCommand CopySourceCommand { get; }

    public RelayCommand CopyTranslatedCommand { get; }

    public RelayCommand SpeakSourceCommand { get; }

    public RelayCommand SpeakTranslatedCommand { get; }

    public RelayCommand ClearSourceCommand { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string SourceText
    {
        get => _sourceText;
        set
        {
            if (SetProperty(ref _sourceText, value))
            {
                UpdateDirectionHint();
                TranslateCommand.RaiseCanExecuteChanged();
                CopySourceCommand.RaiseCanExecuteChanged();
                SpeakSourceCommand.RaiseCanExecuteChanged();
                ClearSourceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string TranslatedText
    {
        get => _translatedText;
        set
        {
            if (SetProperty(ref _translatedText, value))
            {
                CopyTranslatedCommand.RaiseCanExecuteChanged();
                SpeakTranslatedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SourceLanguageCode
    {
        get => _sourceLanguageCode;
        set
        {
            if (SetProperty(ref _sourceLanguageCode, value))
            {
                UpdateDirectionHint();
            }
        }
    }

    public string TargetLanguageCode
    {
        get => _targetLanguageCode;
        set
        {
            if (SetProperty(ref _targetLanguageCode, value))
            {
                UpdateDirectionHint();
            }
        }
    }

    public string DirectionHint
    {
        get => _directionHint;
        private set => SetProperty(ref _directionHint, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                TranslateCommand.RaiseCanExecuteChanged();
                RecaptureCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanRecapture
    {
        get => _canRecapture;
        private set
        {
            if (SetProperty(ref _canRecapture, value))
            {
                RecaptureCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Reset(
        string title,
        string status,
        string sourceText,
        string translatedText,
        AppSettings settings,
        Func<Task>? repeatCaptureAction,
        bool preserveLanguageSelection = false)
    {
        var previousSourceLanguage = SourceLanguageCode;
        var previousTargetLanguage = TargetLanguageCode;

        Settings = settings;
        _repeatCaptureAction = repeatCaptureAction;
        Title = title;
        Status = status;
        SourceLanguageCode = preserveLanguageSelection
            ? previousSourceLanguage
            : TranslationLanguageHelper.AutoLanguage;
        TargetLanguageCode = preserveLanguageSelection
            ? previousTargetLanguage
            : TranslationLanguageHelper.AutoLanguage;
        SourceText = sourceText;
        TranslatedText = translatedText;
        IsBusy = false;
        CanRecapture = repeatCaptureAction is not null;
        RefreshApiProfiles();
    }

    public void RefreshApiProfiles()
    {
        OnPropertyChanged(nameof(ApiProfiles));
        OnPropertyChanged(nameof(SelectedApiProfileId));
    }

    public void SetBusyState(string status)
    {
        Status = status;
        IsBusy = true;
    }

    public void UpdateRecognizedSource(string sourceText, string status)
    {
        SourceText = sourceText;
        Status = status;
        IsBusy = false;
    }

    public void UpdateTranslationResult(string translatedText, string status)
    {
        TranslatedText = translatedText;
        Status = status;
        IsBusy = false;
    }

    public void UpdateFailure(string status, string? translatedText = null)
    {
        if (!string.IsNullOrWhiteSpace(translatedText))
        {
            TranslatedText = translatedText;
        }

        Status = status;
        IsBusy = false;
    }

    private bool CanTranslate()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(SourceText);
    }

    private async Task TranslateAsync()
    {
        var sourceText = SourceText.Trim();
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            Status = "原文为空，无法执行翻译。";
            return;
        }

        SetBusyState("正在翻译...");

        try
        {
            var effectiveSettings = TranslationLanguageHelper.BuildSettingsForTranslation(
                Settings,
                sourceText,
                TargetLanguageCode);

            var stopwatch = Stopwatch.StartNew();
            var result = await _translationService.TranslateAsync(sourceText, effectiveSettings);
            stopwatch.Stop();
            if (result.Success)
            {
                UpdateTranslationResult(
                    result.Text,
                    $"翻译完成，目标语言：{GetSelectedTargetLanguageLabel()}，来源：{SelectedProviderLabel}，耗时：{stopwatch.Elapsed.TotalSeconds:0.0} 秒");
            }
            else
            {
                UpdateFailure($"翻译失败：{result.ErrorMessage}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            UpdateFailure($"翻译执行失败：{ex.Message}");
        }
    }

    private void CopySource()
    {
        Clipboard.SetText(SourceText ?? string.Empty);
        Status = "原文已复制。";
    }

    private async Task RecaptureAsync()
    {
        if (_repeatCaptureAction is null)
        {
            return;
        }

        try
        {
            Status = "请在屏幕上重新框选识别区域。";
            IsBusy = true;
            await _repeatCaptureAction();
        }
        finally
        {
            IsBusy = false;
            CanRecapture = _repeatCaptureAction is not null;
        }
    }

    private void CopyTranslated()
    {
        Clipboard.SetText(TranslatedText ?? string.Empty);
        Status = "译文已复制。";
    }

    private void SpeakSource()
    {
        var language = TranslationLanguageHelper.ResolveSpeechLanguage(
            SourceLanguageCode,
            SourceText,
            TranslationLanguageHelper.English);
        Status = _speechService.Speak(SourceText, language);
    }

    private void SpeakTranslated()
    {
        var targetLanguage = string.Equals(TargetLanguageCode, TranslationLanguageHelper.AutoLanguage, StringComparison.OrdinalIgnoreCase)
            ? GetAutoTargetLanguage(SourceText?.Trim() ?? string.Empty)
            : TargetLanguageCode;
        var language = TranslationLanguageHelper.ResolveSpeechLanguage(
            targetLanguage,
            TranslatedText,
            targetLanguage);
        Status = _speechService.Speak(TranslatedText, language);
    }

    private void ClearSource()
    {
        SourceText = string.Empty;
        TranslatedText = string.Empty;
        Status = "已清空原文。";
    }

    private void UpdateDirectionHint()
    {
        var sourceLabel = TranslationLanguageHelper.GetLanguageLabel(SourceLanguageCode);
        var targetLabel = string.Equals(TargetLanguageCode, TranslationLanguageHelper.AutoLanguage, StringComparison.OrdinalIgnoreCase)
            ? TranslationLanguageHelper.GetLanguageLabel(GetAutoTargetLanguage(SourceText?.Trim() ?? string.Empty))
            : TranslationLanguageHelper.GetLanguageLabel(TargetLanguageCode);

        DirectionHint = $"{sourceLabel} -> {targetLabel}";
    }

    private string GetAutoTargetLanguage(string sourceText)
    {
        if (!string.IsNullOrWhiteSpace(SourceLanguageCode)
            && !string.Equals(SourceLanguageCode, TranslationLanguageHelper.AutoLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(SourceLanguageCode, TranslationLanguageHelper.ChineseSimplified, StringComparison.OrdinalIgnoreCase)
                ? TranslationLanguageHelper.English
                : TranslationLanguageHelper.ChineseSimplified;
        }

        return TranslationLanguageHelper.ResolveTargetLanguage(Settings, sourceText);
    }

    private string GetSelectedTargetLanguageLabel()
    {
        return string.Equals(TargetLanguageCode, TranslationLanguageHelper.AutoLanguage, StringComparison.OrdinalIgnoreCase)
            ? TranslationLanguageHelper.GetLanguageLabel(GetAutoTargetLanguage(SourceText?.Trim() ?? string.Empty))
            : TranslationLanguageHelper.GetLanguageLabel(TargetLanguageCode);
    }
}
