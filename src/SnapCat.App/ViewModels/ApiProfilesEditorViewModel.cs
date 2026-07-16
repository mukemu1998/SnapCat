using System.Collections.ObjectModel;
using SnapCat.Core.Models;

namespace SnapCat.App.ViewModels;

internal sealed class ApiProfilesEditorViewModel : ObservableObject
{
    private string _selectedProfileId = string.Empty;
    private string _editingProfileId = string.Empty;

    public ObservableCollection<ApiProfileEditorItemViewModel> Profiles { get; } = [];

    public string SelectedProfileId
    {
        get => _selectedProfileId;
        set
        {
            if (SetProperty(ref _selectedProfileId, value))
            {
                OnPropertyChanged(nameof(SelectedProfile));
                OnPropertyChanged(nameof(HasProfiles));
            }
        }
    }

    public ApiProfileEditorItemViewModel? SelectedProfile =>
        Profiles.FirstOrDefault(profile => string.Equals(profile.Id, SelectedProfileId, StringComparison.Ordinal));

    public ApiProfileEditorItemViewModel? EditingProfile =>
        Profiles.FirstOrDefault(profile => string.Equals(profile.Id, _editingProfileId, StringComparison.Ordinal));

    public bool HasProfiles => Profiles.Count > 0;

    public void LoadFromSettings(AppSettings settings)
    {
        settings.NormalizeApiProfiles();
        Profiles.Clear();

        foreach (var profile in settings.ApiProfiles)
        {
            Profiles.Add(ApiProfileEditorItemViewModel.FromModel(profile));
        }

        SelectedProfileId = settings.SelectedApiProfileId;
        EnsureValidSelection();
        RefreshState();
    }

    public List<ApiTranslationProfile> ToModels() => Profiles
        .Select(static (profile, index) => profile.ToModel(index))
        .ToList();

    public ApiProfileEditorItemViewModel AddNewProfile()
    {
        var profile = new ApiProfileEditorItemViewModel
        {
            Name = GenerateNextProfileName(),
            SystemPrompt = AppSettings.DefaultSystemPrompt
        };

        Profiles.Add(profile);
        SelectedProfileId = profile.Id;
        RefreshState();
        return profile;
    }

    public ApiProfileEditorItemViewModel AddNewProfileAfterEditingDraft(ApiProfileEditorDraft currentDraft)
    {
        ApplyDraftToEditingProfile(currentDraft);
        return AddNewProfile();
    }

    public ApiProfileEditorItemViewModel? DeleteSelectedProfile()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            return null;
        }

        Profiles.Remove(profile);
        SelectedProfileId = Profiles.FirstOrDefault()?.Id ?? string.Empty;
        if (string.Equals(_editingProfileId, profile.Id, StringComparison.Ordinal))
        {
            _editingProfileId = string.Empty;
        }

        RefreshState();
        return profile;
    }

    public ApiProfileEditorDraft BeginEditingSelectedProfile()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            _editingProfileId = string.Empty;
            return ApiProfileEditorDraft.Empty;
        }

        _editingProfileId = profile.Id;
        return profile.ToDraft();
    }

    public ApiProfilesEditorState PrepareEditorState()
    {
        EnsureValidSelection();
        var draft = BeginEditingSelectedProfile();
        return new ApiProfilesEditorState(HasProfiles, SelectedProfileId, draft);
    }

    public void ApplyDraftToEditingProfile(ApiProfileEditorDraft draft)
    {
        EditingProfile?.ApplyDraft(draft);
    }

    public ApiProfileEditorDraft SelectProfileForEditing(string profileId, ApiProfileEditorDraft currentDraft)
    {
        ApplyDraftToEditingProfile(currentDraft);
        SelectedProfileId = profileId;
        EnsureValidSelection();
        return BeginEditingSelectedProfile();
    }

    public void EnsureValidSelection()
    {
        if (Profiles.Count == 0)
        {
            SelectedProfileId = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfileId)
            || Profiles.All(profile => !string.Equals(profile.Id, SelectedProfileId, StringComparison.Ordinal)))
        {
            SelectedProfileId = Profiles[0].Id;
        }
    }

    public void RefreshState()
    {
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(HasProfiles));
    }

    private string GenerateNextProfileName()
    {
        var index = 1;
        while (Profiles.Any(profile => string.Equals(profile.Name, $"API 配置 {index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"API 配置 {index}";
    }
}
