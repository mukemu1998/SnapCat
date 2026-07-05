namespace SnapCat.App.ViewModels;

internal sealed record ApiProfilesEditorState(
    bool HasProfiles,
    string SelectedProfileId,
    ApiProfileEditorDraft Draft);
