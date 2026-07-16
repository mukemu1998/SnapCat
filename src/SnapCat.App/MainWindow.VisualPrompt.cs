using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using SnapCat.Infrastructure.Services;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsFolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace SnapCat.App;

public partial class MainWindow
{
    private readonly List<AiProviderProfile> _visualPromptProfiles = [];
    public ObservableCollection<string> InstalledVisualModelNames { get; } = [];
    private string _selectedVisualPromptProfileId = string.Empty;
    private string _defaultVisualPromptProfileId = string.Empty;
    private bool _isApplyingVisualPromptProfileState;
    private VisualPromptWindow? _visualPromptWindow;
    private bool _isOllamaOperationRunning;
    private bool _isOllamaServiceAvailable;
    private bool _isRefreshingOllamaModelChoices;
    private CancellationTokenSource? _ollamaDownloadCancellation;
    private string _interruptedOllamaDownloadModelName = string.Empty;

    private void LoadVisualPromptProfiles(AppSettings settings)
    {
        _isApplyingVisualPromptProfileState = true;
        _visualPromptProfiles.Clear();
        _visualPromptProfiles.AddRange(AiProviderProfile.CloneAll(settings.AiProviderProfiles));
        _defaultVisualPromptProfileId = settings.SelectedAiProviderProfileId;
        _selectedVisualPromptProfileId = _defaultVisualPromptProfileId;
        EnsureDefaultVisualPromptProfile();
        RefreshVisualPromptProfileEditor();
        _isApplyingVisualPromptProfileState = false;
    }

    private void RefreshVisualPromptProfileEditor()
    {
        if (VisualPromptProfileExpandersItemsControl is null)
        {
            return;
        }

        VisualPromptProfileExpandersItemsControl.ItemsSource = null;
        VisualPromptProfileExpandersItemsControl.ItemsSource = _visualPromptProfiles;
        var profile = GetSelectedVisualPromptProfile();
        if (profile is null && _visualPromptProfiles.Count > 0)
        {
            profile = _visualPromptProfiles[0];
            _selectedVisualPromptProfileId = profile.Id;
        }

        EmptyVisualPromptProfileTextBlock.Visibility = profile is null ? Visibility.Visible : Visibility.Collapsed;
        DeleteVisualPromptProfileButton.IsEnabled = profile is not null;
        var defaultProfile = GetDefaultVisualPromptProfile();
        VisualPromptDefaultProfileTextBlock.Text = defaultProfile is null
            ? string.Empty
            : $"默认配置：{defaultProfile.Name}（首次框选分析会使用它；浮窗内可临时切换其他允许调用的配置）";
    }

    private AiProviderProfile? GetSelectedVisualPromptProfile()
    {
        return _visualPromptProfiles.FirstOrDefault(profile =>
                   string.Equals(profile.Id, _selectedVisualPromptProfileId, StringComparison.Ordinal))
               ?? _visualPromptProfiles.FirstOrDefault();
    }

    private AiProviderProfile? GetDefaultVisualPromptProfile()
    {
        return _visualPromptProfiles.FirstOrDefault(profile =>
                   string.Equals(profile.Id, _defaultVisualPromptProfileId, StringComparison.Ordinal))
               ?? _visualPromptProfiles.FirstOrDefault(profile => profile.IsEnabled && profile.Supports(AiModelCapabilities.VisionAnalysis));
    }

    private void PersistVisualPromptProfileEditor()
    {
        var profile = GetSelectedVisualPromptProfile();
        if (profile is null)
        {
            return;
        }

        profile.Capabilities |= AiModelCapabilities.VisionAnalysis;
        profile.MaxReferenceImageCount = 1;
        profile.MaxOutputCount = 1;
        profile.Normalize(_visualPromptProfiles.IndexOf(profile));
    }

    private void AddVisualPromptProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        PersistVisualPromptProfileEditor();
        var profile = new AiProviderProfile
        {
            Name = $"视觉配置 {_visualPromptProfiles.Count + 1}",
            Protocol = AiProviderProtocol.Ollama,
            BaseUrl = "http://127.0.0.1:11434",
            Capabilities = AiModelCapabilities.VisionAnalysis,
            MaxReferenceImageCount = 1,
            MaxOutputCount = 1
        };
        _visualPromptProfiles.Add(profile);
        _selectedVisualPromptProfileId = profile.Id;
        if (string.IsNullOrWhiteSpace(_defaultVisualPromptProfileId))
        {
            _defaultVisualPromptProfileId = profile.Id;
        }
        _isApplyingVisualPromptProfileState = true;
        RefreshVisualPromptProfileEditor();
        _isApplyingVisualPromptProfileState = false;
        MarkSettingsDirty();
        StatusTextBlock.Text = $"已添加视觉模型配置：{profile.Name}";
    }

    private void DeleteVisualPromptProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var profile = GetSelectedVisualPromptProfile();
        if (profile is null)
        {
            return;
        }

        _visualPromptProfiles.Remove(profile);
        _selectedVisualPromptProfileId = _visualPromptProfiles.FirstOrDefault()?.Id ?? string.Empty;
        if (string.Equals(_defaultVisualPromptProfileId, profile.Id, StringComparison.Ordinal))
        {
            _defaultVisualPromptProfileId = string.Empty;
            EnsureDefaultVisualPromptProfile();
        }
        _isApplyingVisualPromptProfileState = true;
        RefreshVisualPromptProfileEditor();
        _isApplyingVisualPromptProfileState = false;
        MarkSettingsDirty();
        StatusTextBlock.Text = $"已删除视觉模型配置：{profile.Name}";
    }

    private void VisualPromptProfileExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        if (_isApplyingVisualPromptProfileState
            || sender is not Expander { DataContext: AiProviderProfile profile })
        {
            return;
        }

        _selectedVisualPromptProfileId = profile.Id;
        DeleteVisualPromptProfileButton.IsEnabled = true;
    }

    private void VisualPromptProfileEnabled_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isApplyingVisualPromptProfileState
            || sender is not WpfCheckBox { DataContext: AiProviderProfile profile })
        {
            return;
        }

        profile.Capabilities |= AiModelCapabilities.VisionAnalysis;
        EnsureDefaultVisualPromptProfile();
        RefreshVisualPromptProfileEditor();
        MarkSettingsDirty();
    }

    private void SetDefaultVisualPromptProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element
            || FindParent<Expander>(element) is not { DataContext: AiProviderProfile profile })
        {
            return;
        }

        if (!profile.IsEnabled)
        {
            StatusTextBlock.Text = "请先勾选“允许用于分析”，再将该配置设为默认。";
            return;
        }

        _defaultVisualPromptProfileId = profile.Id;
        RefreshVisualPromptProfileEditor();
        MarkSettingsDirty();
        StatusTextBlock.Text = $"已将“{profile.Name}”设为默认图片提示词分析配置。";
    }

    private async void TestVisualPromptProfileConnectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: AiProviderProfile profile } button)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.BaseUrl) || string.IsNullOrWhiteSpace(profile.Model))
        {
            StatusTextBlock.Text = "请先填写接口地址和视觉模型名称，再检测连接。";
            return;
        }

        button.IsEnabled = false;
        StatusTextBlock.Text = $"正在检测视觉配置“{profile.Name}”的连接与模型可用性...";
        try
        {
            var localServiceMessage = await EnsureLocalOllamaServiceAsync(profile);
            if (!string.IsNullOrWhiteSpace(localServiceMessage))
            {
                StatusTextBlock.Text = $"视觉配置“{profile.Name}”无法连接：{localServiceMessage}";
                return;
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var responseText = await SendVisualPromptProbeAsync(client, profile);
            StatusTextBlock.Text = $"视觉配置“{profile.Name}”连接成功：{responseText}";
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"视觉配置“{profile.Name}”连接失败：{exception.Message}";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async Task<string?> EnsureLocalOllamaServiceAsync(AiProviderProfile profile)
    {
        if (!UsesManagedLocalOllama(profile))
        {
            return null;
        }

        var status = await _app.OllamaRuntimeService.EnsureServiceAvailableAsync();
        _isOllamaServiceAvailable = status.IsServiceAvailable;
        if (status.IsServiceAvailable)
        {
            return null;
        }

        return status.Message;
    }

    private static bool UsesManagedLocalOllama(AiProviderProfile profile)
    {
        if (!string.Equals(AiProviderProtocol.Normalize(profile.Protocol), AiProviderProtocol.Ollama, StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        return (string.Equals(endpoint.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            && endpoint.Port == 11434;
    }

    private static async Task<string> SendVisualPromptProbeAsync(HttpClient client, AiProviderProfile profile)
    {
        var protocol = AiProviderProtocol.Normalize(profile.Protocol);
        var endpoint = protocol == AiProviderProtocol.Ollama
            ? BuildVisualProbeEndpoint(profile.BaseUrl, "api/chat")
            : BuildVisualProbeEndpoint(profile.BaseUrl, "chat/completions");
        object payload = protocol == AiProviderProtocol.Ollama
            ? new
            {
                model = profile.Model,
                stream = false,
                messages = new[] { new { role = "user", content = "Reply with OK." } }
            }
            : new
            {
                model = profile.Model,
                max_tokens = 1,
                messages = new[] { new { role = "user", content = "Reply with OK." } }
            };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        if (protocol != AiProviderProtocol.Ollama && !string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
        }

        using var response = await client.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var detail = string.IsNullOrWhiteSpace(responseText)
                ? response.ReasonPhrase ?? "未知响应"
                : responseText[..Math.Min(responseText.Length, 180)].Replace(Environment.NewLine, " ");
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}：{detail}");
        }

        return protocol == AiProviderProtocol.Ollama
            ? "Ollama 服务与选中模型均可响应。"
            : "兼容接口、密钥与选中模型均可响应。";
    }

    private static Uri BuildVisualProbeEndpoint(string baseUrl, string relativePath)
    {
        var trimmed = baseUrl.Trim();
        var suppliedUri = new Uri(trimmed, UriKind.Absolute);
        if (suppliedUri.AbsolutePath.TrimEnd('/').EndsWith($"/{relativePath}", StringComparison.OrdinalIgnoreCase))
        {
            return suppliedUri;
        }

        return new Uri(new Uri(trimmed.TrimEnd('/') + "/", UriKind.Absolute), relativePath);
    }

    private void EnsureDefaultVisualPromptProfile()
    {
        var selected = _visualPromptProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, _defaultVisualPromptProfileId, StringComparison.Ordinal));
        if (selected is { IsEnabled: true })
        {
            return;
        }

        _defaultVisualPromptProfileId = _visualPromptProfiles
            .FirstOrDefault(profile => profile.IsEnabled && profile.Supports(AiModelCapabilities.VisionAnalysis))?.Id
            ?? _visualPromptProfiles.FirstOrDefault(profile => profile.IsEnabled)?.Id
            ?? string.Empty;
    }

    private void VisualPromptProfileCardTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingVisualPromptProfileState
            || sender is not System.Windows.Controls.TextBox { DataContext: AiProviderProfile profile })
        {
            return;
        }

        _selectedVisualPromptProfileId = profile.Id;
        DeleteVisualPromptProfileButton.IsEnabled = true;
        MarkSettingsDirty();
    }

    private void VisualPromptProfileCardProtocolChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingVisualPromptProfileState
            || sender is not System.Windows.Controls.ComboBox { DataContext: AiProviderProfile profile })
        {
            return;
        }

        profile.Protocol = ((System.Windows.Controls.ComboBox)sender).SelectedValue?.ToString()
            ?? AiProviderProtocol.OpenAiCompatible;
        if (string.Equals(profile.Protocol, AiProviderProtocol.Ollama, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(profile.BaseUrl)
                || profile.BaseUrl.StartsWith("https://api.openai.com", StringComparison.OrdinalIgnoreCase)))
        {
            profile.BaseUrl = "http://127.0.0.1:11434";
        }
        else if (string.Equals(profile.Protocol, AiProviderProtocol.OpenAiCompatible, StringComparison.OrdinalIgnoreCase)
                 && (string.IsNullOrWhiteSpace(profile.BaseUrl)
                     || profile.BaseUrl.StartsWith("http://127.0.0.1:11434", StringComparison.OrdinalIgnoreCase)))
        {
            profile.BaseUrl = "https://api.openai.com/v1";
        }

        _selectedVisualPromptProfileId = profile.Id;
        DeleteVisualPromptProfileButton.IsEnabled = true;
        MarkSettingsDirty();
    }

    private void VisualPromptProfileCardApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (_isApplyingVisualPromptProfileState
            || sender is not PasswordBox { DataContext: AiProviderProfile profile } passwordBox)
        {
            return;
        }

        profile.ApiKey = passwordBox.Password;
        _selectedVisualPromptProfileId = profile.Id;
        DeleteVisualPromptProfileButton.IsEnabled = true;
        MarkSettingsDirty();
    }

    private void VisualPromptProfileCardVisibleApiKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingVisualPromptProfileState
            || sender is not System.Windows.Controls.TextBox { DataContext: AiProviderProfile profile } textBox)
        {
            return;
        }

        profile.ApiKey = textBox.Text;
        if (FindParent<Grid>(textBox) is { } editorGrid
            && editorGrid.Children.OfType<PasswordBox>().FirstOrDefault() is { } passwordBox
            && !string.Equals(passwordBox.Password, profile.ApiKey, StringComparison.Ordinal))
        {
            passwordBox.Password = profile.ApiKey;
        }

        _selectedVisualPromptProfileId = profile.Id;
        DeleteVisualPromptProfileButton.IsEnabled = true;
        MarkSettingsDirty();
    }

    private void ToggleVisualPromptApiKeyVisibilityButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button
            || FindParent<Grid>(button) is not { } editorGrid)
        {
            return;
        }

        var passwordBox = editorGrid.Children.OfType<PasswordBox>().FirstOrDefault();
        var textBox = editorGrid.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault();
        if (passwordBox is null || textBox is null)
        {
            return;
        }

        var reveal = textBox.Visibility != Visibility.Visible;
        if (reveal)
        {
            textBox.Text = passwordBox.Password;
        }

        textBox.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
        passwordBox.Visibility = reveal ? Visibility.Collapsed : Visibility.Visible;
        button.ToolTip = reveal ? "隐藏 API Key" : "显示 API Key";
    }

    private void ApplyInstalledVisualModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string modelName } element
            || FindParent<Expander>(element) is not { DataContext: AiProviderProfile profile }
            || string.IsNullOrWhiteSpace(modelName))
        {
            return;
        }

        profile.Protocol = AiProviderProtocol.Ollama;
        profile.BaseUrl = "http://127.0.0.1:11434";
        profile.ApiKey = string.Empty;
        profile.Model = modelName;
        profile.Capabilities = AiModelCapabilities.VisionAnalysis;
        profile.MaxReferenceImageCount = 1;
        profile.MaxOutputCount = 1;
        _selectedVisualPromptProfileId = profile.Id;
        MarkSettingsDirty();
        StatusTextBlock.Text = $"已将 {modelName} 应用到视觉配置：{profile.Name}";
    }

    private async Task ShowVisualPromptAnalysisAsync(string imagePath, Int32Rect captureRegion)
    {
        PersistVisualPromptProfileEditor();
        EnsureDefaultVisualPromptProfile();
        var profile = GetEnabledVisualPromptProfile(_defaultVisualPromptProfileId);
        if (profile is null || string.IsNullOrWhiteSpace(profile.Model))
        {
            ShowMainWindow();
            SelectSection(MainSection.VisualPromptSettings);
            StatusTextBlock.Text = "请先在“提示词分析”中添加并填写一个已启用的视觉模型配置。";
            return;
        }

        var popup = _visualPromptWindow;
        if (popup is null || !popup.IsLoaded)
        {
            popup = new VisualPromptWindow();
            _visualPromptWindow = popup;
        }

        // This window is reused between captures, so always replace the request.
        popup.ReanalyzeRequested = async () =>
        {
            var selectedProfile = GetEnabledVisualPromptProfile(popup.SelectedProfileId);
            if (selectedProfile is null)
            {
                popup.UpdateResult(string.Empty, "请选择一个允许调用且已填写模型的视觉配置。");
                return;
            }

            await AnalyzeVisualPromptAsync(imagePath, captureRegion, selectedProfile, popup);
        };

        popup.SetAvailableProfiles(
            _visualPromptProfiles
                .Where(candidate => candidate.IsEnabled && candidate.Supports(AiModelCapabilities.VisionAnalysis))
                .Select(candidate => new VisualPromptProfileOption(candidate.Id, candidate.Name)),
            profile.Id);
        popup.Prepare(profile.Name, captureRegion);
        popup.SetBusyState("正在分析图片并整理提示词...");
        popup.ShowNearSelection();
        await AnalyzeVisualPromptAsync(imagePath, captureRegion, profile, popup);
    }

    private async Task AnalyzeVisualPromptAsync(
        string imagePath,
        Int32Rect captureRegion,
        AiProviderProfile profile,
        VisualPromptWindow popup)
    {
        popup.SetActiveProvider(profile.Name);
        popup.SetBusyState("正在分析图片并整理提示词...");
        var localServiceMessage = await EnsureLocalOllamaServiceAsync(profile);
        if (!string.IsNullOrWhiteSpace(localServiceMessage))
        {
            var message = $"本地视觉服务不可用：{localServiceMessage}";
            popup.UpdateResult(string.Empty, message);
            StatusTextBlock.Text = message;
            return;
        }

        var result = await _app.VisualPromptService.AnalyzeAsync(imagePath, profile);
        if (result.Success)
        {
            popup.UpdateResult(result.Analysis.ToEditableText(), $"分析完成，来源：{profile.Name}");
            StatusTextBlock.Text = "图片提示词分析已完成。";
            return;
        }

        popup.UpdateResult(string.Empty, result.ErrorMessage);
        StatusTextBlock.Text = result.ErrorMessage;
    }

    private AiProviderProfile? GetEnabledVisualPromptProfile(string? profileId)
    {
        return _visualPromptProfiles.FirstOrDefault(profile =>
                   profile.IsEnabled
                   && profile.Supports(AiModelCapabilities.VisionAnalysis)
                   && string.Equals(profile.Id, profileId, StringComparison.Ordinal))
               ?? _visualPromptProfiles.FirstOrDefault(profile =>
                   profile.IsEnabled && profile.Supports(AiModelCapabilities.VisionAnalysis));
    }

    private async Task RefreshOllamaRuntimeStatusAsync()
    {
        if (OllamaRuntimeStatusTextBlock is null)
        {
            return;
        }

        var status = await _app.OllamaRuntimeService.ProbeAsync();
        _isOllamaServiceAvailable = status.IsServiceAvailable;
        OllamaRuntimeStatusTextBlock.Text = status.Message;
        OllamaModelsDirectoryTextBox.Text = status.ModelDirectory;
        var modelChoices = BuildOllamaModelChoices(status.InstalledModels);
        InstalledVisualModelNames.Clear();
        foreach (var model in status.InstalledModels.OrderBy(static model => model, StringComparer.OrdinalIgnoreCase))
        {
            InstalledVisualModelNames.Add(model);
        }
        _isRefreshingOllamaModelChoices = true;
        OllamaInstalledModelsComboBox.ItemsSource = modelChoices;
        var existingSelection = OllamaInstalledModelsComboBox.SelectedValue?.ToString();
        var pendingSelection = modelChoices.FirstOrDefault(choice =>
            !choice.IsInstalled
            && string.Equals(choice.ModelName, existingSelection, StringComparison.OrdinalIgnoreCase));
        OllamaInstalledModelsComboBox.SelectedValue = pendingSelection?.ModelName;
        _isRefreshingOllamaModelChoices = false;
        UpdateOllamaModelPlaceholder();
        OpenOllamaDownloadButton.IsEnabled = !status.IsInstalled;
        DownloadQwenVisionModelButton.Visibility = Visibility.Collapsed;
        DownloadQwenVisionModelButton.IsEnabled = false;
        UpdateOllamaModelSelectionActions();
        AppendOllamaLog($"[{DateTime.Now:HH:mm:ss}] {status.Message}");
    }

    private async void RefreshOllamaStatusButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshOllamaRuntimeStatusAsync();
    }

    private void OpenOllamaDownloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://ollama.com/download/windows")
        {
            UseShellExecute = true
        });
        AppendOllamaLog("已打开 Ollama 官方 Windows 下载页。完成安装后返回此页点击“重新检测”。");
    }

    private void ChooseOllamaModelsDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new FormsFolderBrowserDialog
        {
            Description = "选择 Ollama 模型文件存放目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(OllamaModelsDirectoryTextBox.Text)
                ? OllamaModelsDirectoryTextBox.Text
                : OllamaRuntimeService.GetConfiguredModelDirectory(),
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() == FormsDialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            OllamaModelsDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private void ApplyOllamaModelsDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var directory = OllamaModelsDirectoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory))
        {
            AppendOllamaLog("请选择有效的模型存放目录。");
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            OllamaRuntimeService.SetModelDirectory(directory);
            AppendOllamaLog("模型目录已写入当前用户的 OLLAMA_MODELS。请在 Ollama 托盘中退出后重新启动 Ollama，再下载模型。");
        }
        catch (Exception exception)
        {
            AppendOllamaLog($"设置模型目录失败：{exception.Message}");
        }
    }

    private async void DownloadQwenVisionModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (OllamaInstalledModelsComboBox.SelectedItem is not OllamaModelChoice choice)
        {
            AppendOllamaLog("请先选择一个本地视觉模型。");
            return;
        }

        var modelName = choice.ModelName;
        _isOllamaOperationRunning = true;
        _ollamaDownloadCancellation = new CancellationTokenSource();
        DownloadQwenVisionModelButton.IsEnabled = false;
        OllamaDownloadProgressPanel.Visibility = Visibility.Visible;
        OllamaDownloadProgressFillColumn.Width = new GridLength(0, GridUnitType.Star);
        OllamaDownloadProgressRemainderColumn.Width = new GridLength(100, GridUnitType.Star);
        OllamaDownloadProgressTextBlock.Text = $"正在准备下载 {choice.DisplayName}...";
        AppendOllamaLog($"开始下载 {choice.DisplayName}，请保持 Ollama 服务运行并预留足够磁盘空间。");
        try
        {
            await _app.OllamaRuntimeService.PullModelAsync(
                modelName,
                progress => Dispatcher.BeginInvoke(() => UpdateOllamaDownloadProgress(progress)),
                _ollamaDownloadCancellation.Token);
            _interruptedOllamaDownloadModelName = string.Empty;
            AppendOllamaLog($"{modelName} 下载完成。正在刷新模型列表。");
            await RefreshOllamaRuntimeStatusAsync();
        }
        catch (OperationCanceledException)
        {
            _interruptedOllamaDownloadModelName = modelName;
            OllamaDownloadProgressTextBlock.Text = "下载已取消。已完成的部分会由 Ollama 保留，之后选择同一模型即可继续下载。";
            AppendOllamaLog("下载已取消，后续可继续下载，不会从零开始。");
        }
        catch (Exception exception)
        {
            _interruptedOllamaDownloadModelName = modelName;
            OllamaDownloadProgressTextBlock.Text = "下载中断，可在网络恢复后重新选择此模型并点击继续下载。Ollama 会复用已完成的部分。";
            AppendOllamaLog($"模型下载中断：{exception.Message}");
        }
        finally
        {
            _isOllamaOperationRunning = false;
            _ollamaDownloadCancellation?.Dispose();
            _ollamaDownloadCancellation = null;
            await RefreshOllamaRuntimeStatusAsync();
            if (!string.IsNullOrWhiteSpace(_interruptedOllamaDownloadModelName))
            {
                OllamaInstalledModelsComboBox.SelectedValue = _interruptedOllamaDownloadModelName;
            }
        }
    }

    private void CancelOllamaDownloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ollamaDownloadCancellation?.Cancel();
    }

    private void OllamaInstalledModelsComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateOllamaModelPlaceholder();
        UpdateOllamaModelSelectionActions();
        if (_isRefreshingOllamaModelChoices)
        {
            return;
        }

        if (OllamaInstalledModelsComboBox.SelectedItem is not OllamaModelChoice choice)
        {
            return;
        }

        if (!choice.IsInstalled)
        {
            AppendOllamaLog($"{choice.DisplayName} 尚未下载。选择后可点击下载按钮，下载完成后请在上方视觉配置中手动选择它。");
            return;
        }

        AppendOllamaLog($"{choice.DisplayName} 已下载，可在上方视觉配置中选择它。");
        Dispatcher.BeginInvoke(() => OllamaInstalledModelsComboBox.SelectedItem = null);
    }

    private void UpdateOllamaModelPlaceholder()
    {
        if (OllamaModelPlaceholderTextBlock is not null)
        {
            OllamaModelPlaceholderTextBlock.Visibility = OllamaInstalledModelsComboBox.SelectedItem is null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void UpdateOllamaModelSelectionActions()
    {
        if (DownloadQwenVisionModelButton is null)
        {
            return;
        }

        var choice = OllamaInstalledModelsComboBox?.SelectedItem as OllamaModelChoice;
        DownloadQwenVisionModelButton.Visibility = choice is { IsInstalled: false }
            ? Visibility.Visible
            : Visibility.Collapsed;
        DownloadQwenVisionModelButton.IsEnabled = choice is { IsInstalled: false }
            && !_isOllamaOperationRunning
            && _isOllamaServiceAvailable;
        if (choice is { IsInstalled: false })
        {
            DownloadQwenVisionModelButton.Content = string.Equals(
                choice.ModelName,
                _interruptedOllamaDownloadModelName,
                StringComparison.OrdinalIgnoreCase)
                ? $"继续下载 {choice.DisplayName}"
                : $"下载 {choice.DisplayName}";
        }
    }

    private void UpdateOllamaDownloadProgress(OllamaPullProgress progress)
    {
        OllamaDownloadProgressPanel.Visibility = Visibility.Visible;
        if (progress.ProgressPercent is { } percent)
        {
            OllamaDownloadProgressFillColumn.Width = new GridLength(percent, GridUnitType.Star);
            OllamaDownloadProgressRemainderColumn.Width = new GridLength(Math.Max(0, 100 - percent), GridUnitType.Star);
            OllamaDownloadProgressTextBlock.Text = $"{progress.Status} · {percent:F1}% · {FormatBytes(progress.CompletedBytes)} / {FormatBytes(progress.TotalBytes)}";
            return;
        }

        OllamaDownloadProgressTextBlock.Text = progress.Status;
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "--";
        }

        var value = bytes.Value;
        return value >= 1024L * 1024 * 1024
            ? $"{value / 1024d / 1024 / 1024:F2} GB"
            : $"{value / 1024d / 1024:F1} MB";
    }

    private void AppendOllamaLog(string message)
    {
        if (OllamaSetupLogTextBox is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var existingLines = OllamaSetupLogTextBox.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        OllamaSetupLogTextBox.Text = string.Join(
            Environment.NewLine,
            existingLines.Append(message).TakeLast(12));
        OllamaSetupLogTextBox.ScrollToEnd();
    }

    private static List<OllamaModelChoice> BuildOllamaModelChoices(IReadOnlyList<string> installedModels)
    {
        var installed = new HashSet<string>(installedModels, StringComparer.OrdinalIgnoreCase);
        var choices = new List<OllamaModelChoice>
        {
            new("qwen3-vl:2b", "Qwen3-VL 2B · 约 1.9GB", false),
            new("qwen3-vl:4b", "Qwen3-VL 4B · 约 3.3GB", false),
            new("qwen3-vl:8b", "Qwen3-VL 8B · 推荐 · 约 6.1GB", false)
        };

        foreach (var choice in choices)
        {
            choice.IsInstalled = installed.Contains(choice.ModelName);
        }

        foreach (var model in installedModels.Where(model => choices.All(choice => !string.Equals(choice.ModelName, model, StringComparison.OrdinalIgnoreCase))))
        {
            choices.Add(new OllamaModelChoice(model, $"{model} · 已下载", true));
        }

        return choices;
    }

    private sealed class OllamaModelChoice
    {
        public OllamaModelChoice(string modelName, string displayName, bool isInstalled)
        {
            ModelName = modelName;
            DisplayName = displayName;
            IsInstalled = isInstalled;
        }

        public string ModelName { get; }

        public string DisplayName { get; }

        public bool IsInstalled { get; set; }
    }
}
