namespace SnapCat.App.ViewModels;

internal sealed class MainWindowViewModel : ObservableObject
{
    private string _appVersionLabel = string.Empty;
    private string _aboutVersionLabel = string.Empty;
    private string _currentSectionTitle = string.Empty;
    private string _currentSectionDescription = string.Empty;

    public ApiProfilesEditorViewModel ApiProfilesEditor { get; } = new();

    public string AppVersionLabel
    {
        get => _appVersionLabel;
        set => SetProperty(ref _appVersionLabel, value);
    }

    public string AboutVersionLabel
    {
        get => _aboutVersionLabel;
        set => SetProperty(ref _aboutVersionLabel, value);
    }

    public string CurrentSectionTitle
    {
        get => _currentSectionTitle;
        set => SetProperty(ref _currentSectionTitle, value);
    }

    public string CurrentSectionDescription
    {
        get => _currentSectionDescription;
        set => SetProperty(ref _currentSectionDescription, value);
    }

    public void SetVersion(string version)
    {
        AppVersionLabel = $"Preview v{version}";
        AboutVersionLabel = $"版本 {version}";
    }

    public void SetCurrentSection(string title, string description)
    {
        CurrentSectionTitle = title;
        CurrentSectionDescription = description;
    }
}
