using System.Windows;
using System.Windows.Input;

namespace SnapCat.App.Windows;

public partial class ProjectNameDialogWindow : Window
{
    private ProjectNameDialogWindow(string suggestedName)
    {
        InitializeComponent();
        ProjectNameTextBox.Text = suggestedName;
        Loaded += (_, _) =>
        {
            ProjectNameTextBox.Focus();
            ProjectNameTextBox.SelectAll();
        };
    }

    public static bool TryGetName(Window owner, string suggestedName, out string projectName)
    {
        var dialog = new ProjectNameDialogWindow(suggestedName)
        {
            Owner = owner
        };

        var accepted = dialog.ShowDialog() == true;
        projectName = accepted ? dialog.ProjectNameTextBox.Text.Trim() : string.Empty;
        return accepted;
    }

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectNameTextBox.Text))
        {
            ValidationTextBlock.Visibility = Visibility.Visible;
            ProjectNameTextBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
