using System.Windows;
using System.Windows.Controls;
using SnapCat.Core.Models;
using SnapCat.Infrastructure.Services;

namespace SnapCat.App;

public partial class MainWindow
{
    private async void TestTranslationButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = BuildCurrentSettings();
        SetTestButtonsEnabled(false);
        TranslationTestResultTextBox.Text = "正在测试翻译接口，请稍候...";

        try
        {
            const string sourceText = "Hello from SnapCat. This is a translation test.";
            var result = await _app.TranslationService.TranslateAsync(sourceText, settings);

            TranslationTestResultTextBox.Text = result.Success
                ? $"翻译测试成功。\n\n原文：\n{sourceText}\n\n译文：\n{result.Text}"
                : $"翻译测试失败。\n\n错误信息：\n{result.ErrorMessage}";
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
            var message = settings.ApiProfiles.Count == 0
                ? "API 连接测试前请先添加一套 API 配置。"
                : "API 连接测试前请先填写完整的 API Key 和模型。";
            TranslationTestResultTextBox.Text = message;
            StatusTextBlock.Text = message;
            SetTestButtonsEnabled(true);
            return;
        }

        TranslationTestResultTextBox.Text = "正在测试 API 连接，请稍候...";
        StatusTextBlock.Text = "正在测试 API 连接...";

        try
        {
            var testSettings = CloneSettings(settings);
            testSettings.TranslationProviderPreference = TranslationProviderPreference.Api;
            testSettings.NormalizeApiProfiles();
            var selectedProfile = testSettings.GetSelectedApiProfile();

            const string sourceText = "SnapCat API connection test.";
            var result = await _app.TranslationService.TranslateAsync(sourceText, testSettings);

            if (result.Success)
            {
                var message =
                    $"API 连接测试成功。\n\n配置名称：{FormatSummaryValue(selectedProfile?.Name ?? string.Empty)}\n接口地址：{FormatSummaryValue(selectedProfile?.BaseUrl ?? string.Empty)}\n模型：{FormatSummaryValue(selectedProfile?.Model ?? string.Empty)}\n返回内容：\n{result.Text}";
                TranslationTestResultTextBox.Text = message;
                StatusTextBlock.Text = "API 连接测试成功。";
            }
            else
            {
                var message =
                    $"API 连接测试失败。\n\n配置名称：{FormatSummaryValue(selectedProfile?.Name ?? string.Empty)}\n接口地址：{FormatSummaryValue(selectedProfile?.BaseUrl ?? string.Empty)}\n模型：{FormatSummaryValue(selectedProfile?.Model ?? string.Empty)}\n错误信息：\n{result.ErrorMessage}";
                TranslationTestResultTextBox.Text = message;
                StatusTextBlock.Text = "API 连接测试失败。";
            }
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

        SetTranslationProviderSelection(ResolveDefaultTranslationProvider(BuildCurrentSettings()));
    }

    private void ApplyApiProfileState()
    {
        _isApplyingApiProfileState = true;

        if (_editingApiProfiles.Count == 0)
        {
            _selectedApiProfileId = string.Empty;
            ApiProfileCardsListBox.ItemsSource = null;
            ApiProfileManagerGrid.Visibility = Visibility.Collapsed;
            EmptyApiProfileStatePanel.Visibility = Visibility.Visible;
            DeleteApiProfileButton.Visibility = Visibility.Collapsed;
            SetApiEditorVisibility(Visibility.Collapsed);
            ClearApiProfileEditor();
            UpdateApiKeyVisibility(false);
            _isApplyingApiProfileState = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedApiProfileId)
            || _editingApiProfiles.All(profile => !string.Equals(profile.Id, _selectedApiProfileId, StringComparison.Ordinal)))
        {
            _selectedApiProfileId = _editingApiProfiles[0].Id;
        }

        ApiProfileCardsListBox.ItemsSource = null;
        ApiProfileCardsListBox.ItemsSource = _editingApiProfiles;
        ApiProfileCardsListBox.SelectedValue = _selectedApiProfileId;
        ApiProfileManagerGrid.Visibility = Visibility.Visible;
        EmptyApiProfileStatePanel.Visibility = Visibility.Collapsed;
        DeleteApiProfileButton.Visibility = Visibility.Visible;
        SetApiEditorVisibility(Visibility.Visible);
        LoadSelectedApiProfileIntoEditor();
        UpdateApiKeyVisibility(false);
        _isApplyingApiProfileState = false;
    }

    private void LoadSelectedApiProfileIntoEditor()
    {
        var profile = GetSelectedEditingApiProfile();
        if (profile is null)
        {
            ClearApiProfileEditor();
            return;
        }

        ApiProfileNameTextBox.Text = profile.Name;
        BaseUrlTextBox.Text = profile.BaseUrl;
        SetApiKeyValue(profile.ApiKey);
        ModelTextBox.Text = profile.Model;
        SystemPromptTextBox.Text = profile.SystemPrompt;
        ApiProfileEnableContextCheckBox.IsChecked = profile.EnableContext;
    }

    private void PersistCurrentApiProfileEditor()
    {
        var profile = GetSelectedEditingApiProfile();
        if (profile is null)
        {
            return;
        }

        profile.Name = string.IsNullOrWhiteSpace(ApiProfileNameTextBox.Text)
            ? profile.Name
            : ApiProfileNameTextBox.Text.Trim();
        profile.BaseUrl = BaseUrlTextBox.Text.Trim();
        profile.ApiKey = GetCurrentApiKey();
        profile.Model = ModelTextBox.Text.Trim();
        profile.SystemPrompt = string.IsNullOrWhiteSpace(SystemPromptTextBox.Text)
            ? AppSettings.DefaultSystemPrompt
            : SystemPromptTextBox.Text.Trim();
        profile.EnableContext = ApiProfileEnableContextCheckBox.IsChecked == true;
    }

    private ApiTranslationProfile? GetSelectedEditingApiProfile()
    {
        return _editingApiProfiles.FirstOrDefault(profile => string.Equals(profile.Id, _selectedApiProfileId, StringComparison.Ordinal));
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

    private string GenerateNextApiProfileName()
    {
        var index = 1;
        while (_editingApiProfiles.Any(profile => string.Equals(profile.Name, $"API 配置 {index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"API 配置 {index}";
    }

    private void SetApiEditorVisibility(Visibility visibility)
    {
        BaseUrlLabelTextBlock.Visibility = visibility;
        BaseUrlTextBox.Visibility = visibility;
        ApiKeyLabelTextBlock.Visibility = visibility;
        ApiKeyEditorGrid.Visibility = visibility;
        ModelLabelTextBlock.Visibility = visibility;
        ModelTextBox.Visibility = visibility;
        SystemPromptLabelTextBlock.Visibility = visibility;
        SystemPromptTextBox.Visibility = visibility;
    }

    private string GetCurrentApiKey()
    {
        return _isApiKeyVisible ? ApiKeyTextBox.Text : ApiKeyPasswordBox.Password;
    }

    private void SetApiKeyValue(string value)
    {
        _isSyncingApiKeyInputs = true;
        ApiKeyPasswordBox.Password = value ?? string.Empty;
        ApiKeyTextBox.Text = value ?? string.Empty;
        _isSyncingApiKeyInputs = false;
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
        ApiKeyVisibilityIconTextBlock.Text = isVisible ? "🙈" : "👁";
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

        _isSyncingApiKeyInputs = true;
        ApiKeyTextBox.Text = ApiKeyPasswordBox.Password;
        _isSyncingApiKeyInputs = false;
        TranslationProviderInputs_OnTextChanged(ApiKeyTextBox, new TextChangedEventArgs(System.Windows.Controls.TextBox.TextChangedEvent, UndoAction.None));
        UpdateSaveButtonVisibility();
    }

    private void ApiKeyTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingApiKeyInputs)
        {
            TranslationProviderInputs_OnTextChanged(sender, e);
            return;
        }

        _isSyncingApiKeyInputs = true;
        ApiKeyPasswordBox.Password = ApiKeyTextBox.Text;
        _isSyncingApiKeyInputs = false;
        TranslationProviderInputs_OnTextChanged(sender, e);
        UpdateSaveButtonVisibility();
    }

    private void ApiProfileNameTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingApiProfileState)
        {
            return;
        }

        var profile = GetSelectedEditingApiProfile();
        if (profile is not null)
        {
            profile.Name = string.IsNullOrWhiteSpace(ApiProfileNameTextBox.Text)
                ? profile.Name
                : ApiProfileNameTextBox.Text.Trim();
            ApiProfileCardsListBox.Items.Refresh();
        }

        UpdateSaveButtonVisibility();
    }

    private void ApiProfileModelTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingApiProfileState)
        {
            return;
        }

        var profile = GetSelectedEditingApiProfile();
        if (profile is not null)
        {
            profile.Model = ModelTextBox.Text.Trim();
            ApiProfileCardsListBox.Items.Refresh();
        }
    }

    private void ApiProfileEnableContextCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isApplyingApiProfileState)
        {
            return;
        }

        var profile = GetSelectedEditingApiProfile();
        if (profile is not null)
        {
            profile.EnableContext = ApiProfileEnableContextCheckBox.IsChecked == true;
        }

        UpdateSaveButtonVisibility();
    }

    private void TranslationProviderComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTranslationProviderEvents)
        {
            return;
        }

        _translationProviderSelectionTouched = true;
        UpdateSaveButtonVisibility();
    }

    private void AddApiProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        PersistCurrentApiProfileEditor();

        var profile = new ApiTranslationProfile
        {
            Name = GenerateNextApiProfileName(),
            SystemPrompt = AppSettings.DefaultSystemPrompt
        };

        _editingApiProfiles.Add(profile);
        _selectedApiProfileId = profile.Id;
        ApplyApiProfileState();
        UpdateSaveButtonVisibility();
        StatusTextBlock.Text = $"已添加新的 API 配置：{profile.Name}";
    }

    private void DeleteApiProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteApiProfileById(_selectedApiProfileId);
    }

    private void DeleteApiProfileById(string profileId)
    {
        var profile = _editingApiProfiles.FirstOrDefault(item => string.Equals(item.Id, profileId, StringComparison.Ordinal));
        if (profile is null)
        {
            return;
        }

        _editingApiProfiles.RemoveAll(item => string.Equals(item.Id, profile.Id, StringComparison.Ordinal));
        _selectedApiProfileId = _editingApiProfiles.FirstOrDefault()?.Id ?? string.Empty;
        ApplyApiProfileState();

        if (_editingApiProfiles.Count == 0)
        {
            _suppressTranslationProviderEvents = true;
            SetTranslationProviderSelection(TranslationProviderPreference.Local);
            _suppressTranslationProviderEvents = false;
        }

        UpdateSaveButtonVisibility();
        StatusTextBlock.Text = $"已删除 API 配置：{profile.Name}";
    }

    private void ApiProfileCardsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingApiProfileState)
        {
            return;
        }

        PersistCurrentApiProfileEditor();
        _selectedApiProfileId = ApiProfileCardsListBox.SelectedValue?.ToString() ?? string.Empty;
        LoadSelectedApiProfileIntoEditor();
        UpdateSaveButtonVisibility();
    }
}
