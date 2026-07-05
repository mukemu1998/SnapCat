using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using Clipboard = System.Windows.Clipboard;
using WpfButton = System.Windows.Controls.Button;

namespace SnapCat.App.Windows;

public partial class TranslationPopupWindow
{
    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PopupBorder_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && IsInteractiveElement(source))
        {
            return;
        }

        DragMove();
    }

    private async void TranslateButton_OnClick(object sender, RoutedEventArgs e)
    {
        var sourceText = SourceTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            StatusTextBlock.Text = "原文为空，无法执行翻译。";
            return;
        }

        SetBusyState("正在翻译...");

        try
        {
            var effectiveSettings = TranslationLanguageHelper.BuildSettingsForTranslation(
                _settings,
                sourceText,
                TargetLanguageComboBox.SelectedValue as string);

            var result = await _app.TranslationService.TranslateAsync(sourceText, effectiveSettings);
            if (result.Success)
            {
                UpdateTranslationResult(
                    result.Text,
                    $"翻译完成，目标语言：{GetSelectedTargetLanguageLabel()}，来源：{GetSelectedProviderLabel()}");
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

    private void LanguageComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdateDirectionHint();
    }

    private void SourceTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateDirectionHint();
        }
    }

    private void ProviderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton button && button.Tag is string provider)
        {
            SetTranslationProvider(provider);
            StatusTextBlock.Text = $"已切换为{GetSelectedProviderLabel()}。";
        }
    }

    private void ApiProfileComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingApiProfileSelection)
        {
            return;
        }

        _settings.SelectedApiProfileId = ApiProfileComboBox.SelectedValue?.ToString() ?? string.Empty;
        _settings.NormalizeApiProfiles();
        _settings.SyncLegacyApiFieldsFromSelectedProfile();

        if (string.Equals(_settings.TranslationProviderPreference, TranslationProviderPreference.Api, StringComparison.OrdinalIgnoreCase))
        {
            StatusTextBlock.Text = $"已切换 API 配置：{_settings.GetSelectedApiProfile()?.Name ?? "未命名配置"}";
        }
    }

    private void TranslatedTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            AdjustTranslatedTextBoxHeight();
        }
    }

    private void CopySourceButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(SourceTextBox.Text ?? string.Empty);
        StatusTextBlock.Text = "原文已复制。";
    }

    private async void RecaptureButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_repeatCaptureAction is null)
        {
            return;
        }

        RecaptureButton.IsEnabled = false;

        try
        {
            StatusTextBlock.Text = "请在屏幕上重新框选识别区域。";
            await _repeatCaptureAction();
        }
        finally
        {
            RecaptureButton.IsEnabled = _repeatCaptureAction is not null;
        }
    }

    private void CopyTranslatedButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TranslatedTextBox.Text ?? string.Empty);
        StatusTextBlock.Text = "译文已复制。";
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static bool IsInteractiveElement(DependencyObject source)
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (current is System.Windows.Controls.Button
                or System.Windows.Controls.TextBox
                or System.Windows.Controls.ComboBox
                or System.Windows.Controls.Primitives.ScrollBar
                or System.Windows.Controls.Primitives.Thumb)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
