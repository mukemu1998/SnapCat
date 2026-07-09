using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
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

    private void ProviderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton button && button.Tag is string provider)
        {
            SetTranslationProvider(provider);
            _viewModel.Status = $"已切换为{GetSelectedProviderLabel()}。";
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
            _viewModel.Status = $"已切换 API 配置：{_settings.GetSelectedApiProfile()?.Name ?? "未命名配置"}";
        }
        UpdateViewModelTranslationContext();
    }

    private void TranslatedTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            AdjustTranslatedTextBoxHeight();
        }
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
                or System.Windows.Controls.ComboBoxItem
                or ToggleButton
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
