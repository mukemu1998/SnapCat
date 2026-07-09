using System.Windows;
using System.Windows.Input;

namespace SnapCat.App.Windows;

public partial class ConfirmDialogWindow : Window
{
    private ConfirmDialogWindow(string title, string message, string confirmText, string cancelText)
    {
        InitializeComponent();

        Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
    }

    public static bool Confirm(
        Window owner,
        string title,
        string message,
        string confirmText = "确认",
        string cancelText = "取消")
    {
        var dialog = new ConfirmDialogWindow(title, message, confirmText, cancelText)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true;
    }

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
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
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
    }
}
