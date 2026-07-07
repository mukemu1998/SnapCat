namespace SnapCat.App.ViewModels;

internal sealed class MainWindowViewModel : ObservableObject
{
    private string _appVersionLabel = string.Empty;
    private string _aboutVersionLabel = string.Empty;
    private string _aboutReleaseNoteTitle = string.Empty;
    private string _aboutReleaseNoteSummary = string.Empty;
    private IReadOnlyList<string> _aboutReleaseNoteHighlights = Array.Empty<string>();
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

    public string AboutReleaseNoteTitle
    {
        get => _aboutReleaseNoteTitle;
        set => SetProperty(ref _aboutReleaseNoteTitle, value);
    }

    public string AboutReleaseNoteSummary
    {
        get => _aboutReleaseNoteSummary;
        set => SetProperty(ref _aboutReleaseNoteSummary, value);
    }

    public IReadOnlyList<string> AboutReleaseNoteHighlights
    {
        get => _aboutReleaseNoteHighlights;
        set => SetProperty(ref _aboutReleaseNoteHighlights, value);
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

    public void SetAboutReleaseNotes(string title, string summary, IReadOnlyList<string> highlights)
    {
        AboutReleaseNoteTitle = title;
        AboutReleaseNoteSummary = summary;
        AboutReleaseNoteHighlights = highlights;
    }

    public void SetCurrentSection(string title, string description)
    {
        CurrentSectionTitle = title;
        CurrentSectionDescription = description;
    }
}
