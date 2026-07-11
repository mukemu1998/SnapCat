using System.Windows;
using System.Windows.Controls;
using SnapCat.App.Services;
using SnapCat.App.ViewModels;
using SnapCat.Core.Models;
using SnapCat.Infrastructure.Services;

namespace SnapCat.App;

public partial class MainWindow
{
    private ApiProfilesEditorViewModel ApiProfilesEditor => _viewModel.ApiProfilesEditor;

    private async void TestTranslationButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = BuildCurrentSettings();
        SetTestButtonsEnabled(false);
        TranslationTestResultTextBox.Text = "正在测试翻译接口，请稍候...";

        try
        {
            const string sourceText = "Hello from SnapCat. This is a translation test.";
            var result = await _app.TranslationService.TranslateAsync(sourceText, settings);
            TranslationTestResultTextBox.Text = TranslationTestMessageFormatter.BuildTranslationTestResult(sourceText, result);
        }
        catch (Exception ex)
        {
            TranslationTestResultTextBox.Text = $"翻译测试执行失败：{ex.Message}";
        }
        finally
        {
            SetTestButtonsEnabled(true);
        }
    }

    private async void TestApiConnectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = BuildCurrentSettings();
        SetTestButtonsEnabled(false);

        if (!SmartTranslationService.HasCustomApiSettings(settings))
        {
            var message = TranslationTestMessageFormatter.BuildApiMissingConfigurationMessage(settings);
            TranslationTestResultTextBox.Text = message;
            StatusTextBlock.Text = message;
            SetTestButtonsEnabled(true);
            return;
        }

        TranslationTestResultTextBox.Text = "正在测试 API 连接，请稍候...";
        StatusTextBlock.Text = "正在测试 API 连接...";

        try
        {
            var testSettings = TranslationLanguageHelper.CloneSettings(settings);
            testSettings.TranslationProviderPreference = TranslationProviderPreference.Api;
            testSettings.NormalizeApiProfiles();
            var selectedProfile = testSettings.GetSelectedApiProfile();

            const string sourceText = "SnapCat API connection test.";
            var result = await _app.TranslationService.TranslateAsync(sourceText, testSettings);

            TranslationTestResultTextBox.Text = TranslationTestMessageFormatter.BuildApiConnectionResult(selectedProfile, result);
            StatusTextBlock.Text = result.Success ? "API 连接测试成功。" : "API 连接测试失败。";
        }
        catch (Exception ex)
        {
            TranslationTestResultTextBox.Text = $"API 连接测试执行失败：{ex.Message}";
            StatusTextBlock.Text = "API 连接测试执行失败。";
        }
        finally
        {
            SetTestButtonsEnabled(true);
        }
    }

    private void TranslationProviderInputs_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTranslationProviderEvents || _translationProviderSelectionTouched)
        {
            return;
        }

        SetTranslationProviderSelection(ResolveDefaultTranslationProviderFromEditor());
    }

    private string ResolveDefaultTranslationProviderFromEditor()
    {
        return ApiProfilesEditor.HasProfiles
            && !string.IsNullOrWhiteSpace(GetCurrentApiKey())
            && !string.IsNullOrWhiteSpace(ModelTextBox.Text)
            ? TranslationProviderPreference.Api
            : TranslationProviderPreference.Local;
    }

    private void ApplyApiProfileState()
    {
        _isApplyingApiProfileState = true;
        try
        {
            var state = ApiProfilesEditor.PrepareEditorState();
            if (!state.HasProfiles)
            {
                ApiProfileManagerGrid.Visibility = Visibility.Collapsed;
                EmptyApiProfileStatePanel.Visibility = Visibility.Visible;
                DeleteApiProfileButton.Visibility = Visibility.Visible;
                DeleteApiProfileButton.IsEnabled = false;
                SetApiEditorVisibility(Visibility.Collapsed);
                ClearApiProfileEditor();
                UpdateApiKeyVisibility(false);
                return;
            }

            ApiProfileCardsListBox.SelectedValue = state.SelectedProfileId;
            ApiProfileManagerGrid.Visibility = Visibility.Visible;
            EmptyApiProfileStatePanel.Visibility = Visibility.Collapsed;
            DeleteApiProfileButton.Visibility = Visibility.Visible;
            DeleteApiProfileButton.IsEnabled = !string.IsNullOrWhiteSpace(state.SelectedProfileId);
            ApplyApiProfileDraftToEditor(state.Draft);
            UpdateApiKeyVisibility(false);
            ApiProfileEditorHeaderTextBlock.Text = string.IsNullOrWhiteSpace(state.Draft.Name)
                ? "编辑当前翻译 API 配置"
                : $"编辑翻译 API 配置：{state.Draft.Name}";
            SetApiEditorVisibility(ApiProfileEditorExpander.IsExpanded ? Visibility.Visible : Visibility.Collapsed);
        }
        finally
        {
            _isApplyingApiProfileState = false;
        }
    }

    private void LoadSelectedApiProfileIntoEditor()
    {
        ApplyApiProfileDraftToEditor(ApiProfilesEditor.PrepareEditorState().Draft);
    }

    private void PersistCurrentApiProfileEditor()
    {
        ApiProfilesEditor.ApplyDraftToEditingProfile(BuildApiProfileEditorDraft());
    }

    private ApiProfileEditorDraft BuildApiProfileEditorDraft()
    {
        return new ApiProfileEditorDraft(
            ApiProfileNameTextBox.Text,
            BaseUrlTextBox.Text,
            GetCurrentApiKey(),
            ModelTextBox.Text,
            SystemPromptTextBox.Text,
            ApiProfileEnableContextCheckBox.IsChecked == true);
    }

    private void ApplyApiProfileDraftToEditor(ApiProfileEditorDraft draft)
    {
        ApiProfileNameTextBox.Text = draft.Name;
        BaseUrlTextBox.Text = draft.BaseUrl;
        SetApiKeyValue(draft.ApiKey);
        ModelTextBox.Text = draft.Model;
        SystemPromptTextBox.Text = draft.SystemPrompt;
        ApiProfileEnableContextCheckBox.IsChecked = draft.EnableContext;
    }

    private void ClearApiProfileEditor()
    {
        ApiProfileNameTextBox.Text = string.Empty;
        BaseUrlTextBox.Text = string.Empty;
        SetApiKeyValue(string.Empty);
        ModelTextBox.Text = string.Empty;
        SystemPromptTextBox.Text = AppSettings.DefaultSystemPrompt;
        ApiProfileEnableContextCheckBox.IsChecked = false;
    }

    private void SetApiEditorVisibility(Visibility visibility)
    {
        foreach (var element in GetApiEditorElements())
        {
            element.Visibility = visibility;
        }
    }

    private IEnumerable<UIElement> GetApiEditorElements()
    {
        yield return ApiProfileNameLabelTextBlock;
        yield return ApiProfileNameTextBox;
        yield return BaseUrlLabelTextBlock;
        yield return BaseUrlTextBox;
        yield return ApiKeyLabelTextBlock;
        yield return ApiKeyEditorGrid;
        yield return ModelLabelTextBlock;
        yield return ModelTextBox;
        yield return SystemPromptLabelTextBlock;
        yield return SystemPromptTextBox;
        yield return ApiProfileEnableContextLabelTextBlock;
        yield return ApiProfileEnableContextCheckBox;
    }

    private string GetCurrentApiKey()
    {
        return _isApiKeyVisible ? ApiKeyTextBox.Text : ApiKeyPasswordBox.Password;
    }

    private void SetApiKeyValue(string value)
    {
        _isSyncingApiKeyInputs = true;
        try
        {
            ApiKeyPasswordBox.Password = value ?? string.Empty;
            ApiKeyTextBox.Text = value ?? string.Empty;
        }
        finally
        {
            _isSyncingApiKeyInputs = false;
        }
    }

    private void UpdateApiKeyVisibility(bool isVisible)
    {
        _isApiKeyVisible = isVisible;

        if (ApiKeyPasswordBox is null || ApiKeyTextBox is null || ToggleApiKeyVisibilityButton is null || ApiKeyVisibilityIconTextBlock is null)
        {
            return;
        }

        ApiKeyPasswordBox.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
        ApiKeyTextBox.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ToggleApiKeyVisibilityButton.ToolTip = isVisible ? "隐藏 API Key" : "显示 API Key";
        ApiKeyVisibilityIconTextBlock.Text = "\uE890";
    }

    private void ToggleApiKeyVisibilityButton_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateApiKeyVisibility(!_isApiKeyVisible);
    }

    private void ApiKeyPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncingApiKeyInputs)
        {
            return;
        }

        SyncApiKeyInputs(() => ApiKeyTextBox.Text = ApiKeyPasswordBox.Password);
        TranslationProviderInputs_OnTextChanged(ApiKeyTextBox, new TextChangedEventArgs(System.Windows.Controls.TextBox.TextChangedEvent, UndoAction.None));
        UpdateCurrentApiProfileDraftFromEditor();
    }

    private void ApiKeyTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingApiKeyInputs)
        {
            TranslationProviderInputs_OnTextChanged(sender, e);
            return;
        }

        SyncApiKeyInputs(() => ApiKeyPasswordBox.Password = ApiKeyTextBox.Text);
        TranslationProviderInputs_OnTextChanged(sender, e);
        UpdateCurrentApiProfileDraftFromEditor();
    }

    private void SyncApiKeyInputs(Action syncAction)
    {
        _isSyncingApiKeyInputs = true;
        try
        {
            syncAction();
        }
        finally
        {
            _isSyncingApiKeyInputs = false;
        }
    }

    private void ApiProfileEditor_OnChanged(object sender, RoutedEventArgs e)
    {
        UpdateCurrentApiProfileDraftFromEditor();
    }

    private void UpdateCurrentApiProfileDraftFromEditor()
    {
        if (_isApplyingApiProfileState)
        {
            return;
        }

        ApiProfilesEditor.ApplyDraftToEditingProfile(BuildApiProfileEditorDraft());
        MarkSettingsDirty();
    }

    private void TranslationProviderComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTranslationProviderEvents)
        {
            return;
        }

        _translationProviderSelectionTouched = true;
        MarkSettingsDirty();
    }

    private void AddApiProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var profile = ApiProfilesEditor.AddNewProfileAfterEditingDraft(BuildApiProfileEditorDraft());
        ApplyApiProfileState();
        ApiProfileEditorExpander.IsExpanded = true;
        MarkSettingsDirty();
        StatusTextBlock.Text = $"已添加新的 API 配置：{profile.Name}";
    }

    private void DeleteApiProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteSelectedApiProfile();
    }

    private void DeleteSelectedApiProfile()
    {
        var profile = ApiProfilesEditor.DeleteSelectedProfile();
        if (profile is null)
        {
            return;
        }

        ApplyApiProfileState();

        if (!ApiProfilesEditor.HasProfiles)
        {
            _suppressTranslationProviderEvents = true;
            SetTranslationProviderSelection(TranslationProviderPreference.Local);
            _suppressTranslationProviderEvents = false;
        }

        MarkSettingsDirty();
        StatusTextBlock.Text = $"已删除 API 配置：{profile.Name}";
    }

    private void ApiProfileCardsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingApiProfileState)
        {
            return;
        }

        var draft = ApiProfilesEditor.SelectProfileForEditing(
            ApiProfileCardsListBox.SelectedValue?.ToString() ?? string.Empty,
            BuildApiProfileEditorDraft());
        ApplyApiProfileDraftToEditor(draft);
        ApiProfileEditorHeaderTextBlock.Text = string.IsNullOrWhiteSpace(draft.Name)
            ? "编辑当前翻译 API 配置"
            : $"编辑翻译 API 配置：{draft.Name}";
        ApiProfileEditorExpander.IsExpanded = true;
        DeleteApiProfileButton.IsEnabled = !string.IsNullOrWhiteSpace(ApiProfileCardsListBox.SelectedValue?.ToString());
        MarkSettingsDirty();
    }

    private void ApiProfileEditorExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        if (ApiProfilesEditor.HasProfiles)
        {
            SetApiEditorVisibility(Visibility.Visible);
        }
    }

    private void ApiProfileEditorExpander_OnCollapsed(object sender, RoutedEventArgs e)
    {
        SetApiEditorVisibility(Visibility.Collapsed);
    }
}
