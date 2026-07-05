using System.Windows;
using WpfButton = System.Windows.Controls.Button;

namespace SnapCat.App;

internal sealed record NavigationSectionMetadata(
    WpfButton Button,
    FrameworkElement Content,
    string Title,
    string Description);
