using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace SnapCat.App.Services;

public static class ComboBoxSelectionHelper
{
    public static bool SelectByTag(WpfComboBox comboBox, string value, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        foreach (var item in comboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), value, comparison))
            {
                comboBox.SelectedItem = item;
                return true;
            }
        }

        return false;
    }

    public static string GetSelectedTag(WpfComboBox comboBox, string fallback)
    {
        return (comboBox.SelectedItem as WpfComboBoxItem)?.Tag?.ToString() ?? fallback;
    }

    public static void SelectByTagOrCreateCustom(WpfComboBox comboBox, string value, string customPrefix)
    {
        if (SelectByTag(comboBox, value))
        {
            return;
        }

        var customItem = comboBox.Items
            .OfType<WpfComboBoxItem>()
            .FirstOrDefault(item => item.Tag is null);

        if (customItem is null)
        {
            customItem = new WpfComboBoxItem();
            comboBox.Items.Add(customItem);
        }

        customItem.Tag = value;
        customItem.Content = $"{customPrefix}（{value}）";
        comboBox.SelectedItem = customItem;
    }
}
