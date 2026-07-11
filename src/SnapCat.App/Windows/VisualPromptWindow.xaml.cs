using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;
using SnapCat.Core.Models;
using Clipboard = System.Windows.Clipboard;
using FormsScreen = System.Windows.Forms.Screen;
using DrawingPoint = System.Drawing.Point;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Windows;

public partial class VisualPromptWindow : Window
{
    private Int32Rect? _captureRegion;

    public VisualPromptWindow()
    {
        InitializeComponent();
    }

    // The host replaces this for each new capture so a reused popup never analyzes a stale image.
    public Func<Task>? ReanalyzeRequested { get; set; }

    public string SelectedProfileId => ProfileComboBox.SelectedValue?.ToString() ?? string.Empty;

    public void SetAvailableProfiles(IEnumerable<VisualPromptProfileOption> profiles, string selectedProfileId)
    {
        var options = profiles.ToList();
        ProfileComboBox.ItemsSource = options;
        ProfileComboBox.SelectedValue = options.Any(option => string.Equals(option.Id, selectedProfileId, StringComparison.Ordinal))
            ? selectedProfileId
            : options.FirstOrDefault()?.Id;
        // Keep the selector interactive whenever there is a usable profile.
        ProfileComboBox.IsEnabled = options.Count > 0;
    }

    public void Prepare(string providerName, Int32Rect captureRegion)
    {
        _captureRegion = captureRegion;
        ProviderTextBlock.Text = $"模型配置：{providerName}";
        ResultTextBox.Text = string.Empty;
    }

    public void SetActiveProvider(string providerName)
    {
        ProviderTextBlock.Text = $"模型配置：{providerName}";
    }

    public void SetBusyState(string status)
    {
        StatusTextBlock.Text = status;
        ReanalyzeButton.IsEnabled = false;
    }

    public void UpdateResult(string content, string status)
    {
        ResultTextBox.Text = content;
        StatusTextBlock.Text = status;
        ReanalyzeButton.IsEnabled = true;
    }

    public void ShowNearSelection()
    {
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        if (_captureRegion is { } region)
        {
            PositionNearSelection(region);
        }

        Activate();
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            e.Handled = true;
            DragMove();
        }
    }

    private void WindowSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source
            && (FindParent<System.Windows.Controls.Button>(source) is not null
                || FindParent<System.Windows.Controls.TextBox>(source) is not null
                || FindParent<System.Windows.Controls.ComboBox>(source) is not null))
        {
            return;
        }

        e.Handled = true;
        DragMove();
    }

    private void CopyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ResultTextBox.Text))
        {
            Clipboard.SetText(ResultTextBox.Text);
            StatusTextBlock.Text = "提示词已复制到剪贴板。";
        }
    }

    private async void ReanalyzeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ReanalyzeRequested is null)
        {
            UpdateResult(ResultTextBox.Text, "当前图片没有可重新分析的任务。请关闭浮窗后重新执行框选分析。");
            return;
        }

        SetBusyState("正在使用当前分析配置重新分析图片...");
        try
        {
            await ReanalyzeRequested.Invoke();
        }
        catch (Exception exception)
        {
            UpdateResult(ResultTextBox.Text, $"重新分析失败：{exception.Message}");
        }
    }

    private void ProfileComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileComboBox.SelectedItem is VisualPromptProfileOption option)
        {
            ProviderTextBlock.Text = $"模型配置：{option.DisplayName}（切换后点击“重新分析”执行）";
            StatusTextBlock.Text = "已切换分析配置，点击“重新分析”后使用新配置。";
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void PositionNearSelection(Int32Rect region)
    {
        UpdateLayout();
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? System.Windows.Media.Matrix.Identity;
        var screen = FormsScreen.FromPoint(new DrawingPoint(
            region.X + Math.Max(1, region.Width / 2),
            region.Y + Math.Max(1, region.Height / 2)));
        var workTopLeft = fromDevice.Transform(new WpfPoint(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var workBottomRight = fromDevice.Transform(new WpfPoint(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        var workArea = new WpfRect(workTopLeft, workBottomRight);
        MaxHeight = Math.Max(MinHeight, workArea.Height - 24);
        MaxWidth = Math.Max(MinWidth, workArea.Width - 24);
        UpdateLayout();
        var anchorTopLeft = fromDevice.Transform(new WpfPoint(region.X, region.Y));
        var anchorBottomRight = fromDevice.Transform(new WpfPoint(region.X + region.Width, region.Y + region.Height));
        var anchor = new WpfRect(anchorTopLeft, anchorBottomRight);
        var popupWidth = ActualWidth > 0 ? ActualWidth : Width;
        var popupHeight = ActualHeight > 0 ? ActualHeight : Height;
        var gap = 12d;
        var candidates = new[]
        {
            new WpfPoint(anchor.Right + gap, anchor.Top),
            new WpfPoint(anchor.Left - popupWidth - gap, anchor.Top),
            new WpfPoint(anchor.Left, anchor.Bottom + gap),
            new WpfPoint(anchor.Left, anchor.Top - popupHeight - gap)
        };
        var target = candidates.FirstOrDefault(point =>
            point.X >= workArea.Left
            && point.Y >= workArea.Top
            && point.X + popupWidth <= workArea.Right
            && point.Y + popupHeight <= workArea.Bottom);
        if (!candidates.Any(point => point == target
            && point.X >= workArea.Left
            && point.Y >= workArea.Top
            && point.X + popupWidth <= workArea.Right
            && point.Y + popupHeight <= workArea.Bottom))
        {
            target = candidates[0];
        }

        Left = Math.Clamp(target.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - popupWidth));
        Top = Math.Clamp(target.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - popupHeight));
    }

    private static T? FindParent<T>(DependencyObject source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}

public sealed record VisualPromptProfileOption(string Id, string DisplayName);
