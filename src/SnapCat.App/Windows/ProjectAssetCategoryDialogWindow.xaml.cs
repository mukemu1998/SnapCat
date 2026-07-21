using System.Windows;
using System.Windows.Controls;
using SnapCat.Core.Models;
using WpfRadioButton = System.Windows.Controls.RadioButton;

namespace SnapCat.App.Windows;

public partial class ProjectAssetCategoryDialogWindow : Window
{
    private ProjectAssetCategoryDialogWindow(int itemCount, string actionText)
    {
        InitializeComponent();
        DescriptionTextBlock.Text = itemCount == 1
            ? "请为这张图片选择分类。可保留“未分类”，之后也能在右键菜单中修改。"
            : $"请为选中的 {itemCount} 张图片统一选择分类。之后仍可通过右键批量调整。";
        ConfirmButton.Content = actionText;
    }

    public ProjectAssetCategory SelectedCategory { get; private set; } = ProjectAssetCategory.Unclassified;

    public string SelectedCustomCategory { get; private set; } = string.Empty;

    public static ProjectAssetCategorySelection? Select(Window owner, int itemCount, string actionText = "确认导入")
    {
        var dialog = new ProjectAssetCategoryDialogWindow(itemCount, actionText) { Owner = owner };
        return dialog.ShowDialog() == true
            ? new ProjectAssetCategorySelection(dialog.SelectedCategory, dialog.SelectedCustomCategory)
            : null;
    }

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var candidate in FindVisualChildren<WpfRadioButton>(this))
        {
            if (candidate.IsChecked == true && candidate.Tag is string tag &&
                Enum.TryParse<ProjectAssetCategory>(tag, out var category))
            {
                SelectedCategory = category;
                break;
            }
        }

        SelectedCustomCategory = CustomCategoryTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(SelectedCustomCategory))
        {
            SelectedCategory = ProjectAssetCategory.Unclassified;
        }

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}

public sealed record ProjectAssetCategorySelection(ProjectAssetCategory Category, string CustomCategory);
