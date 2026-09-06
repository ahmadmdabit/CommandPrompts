using System.Collections.ObjectModel;

using CommandPrompts.App.Avalonia.Services;
using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CommandPrompts.App.Avalonia.ViewModels;

/// <summary>
/// Editable snapshot of one env profile (list row + edit source).
/// </summary>
public sealed class EditableProfile
{
    public EditableProfile(string name, IReadOnlyDictionary<string, string> variables)
    {
        Name = name;
        Variables = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);
    }

    public string Name { get; }
    public Dictionary<string, string> Variables { get; }
}

/// <summary>
/// In-app CRUD for shared env profiles. Dispatcher-free so plain NUnit tests
/// can drive it; in the app all commands run on the UI thread.
/// </summary>
public partial class ProfileEditorViewModel : ObservableObject
{
    private readonly EnvProfileService profileService;
    private readonly IDialogService? dialogs;
    private string? selectedOriginalName;

    public ProfileEditorViewModel(EnvProfileService service, IDialogService? dialogService = null)
    {
        profileService = service;
        dialogs = dialogService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        NewCommand = new RelayCommand(NewProfile);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);

        ReloadFromService();
    }

    public ObservableCollection<EditableProfile> Profiles { get; } = [];

    [ObservableProperty]
    private EditableProfile? selectedProfile;

    [ObservableProperty]
    private string editName = string.Empty;

    [ObservableProperty]
    private string editVariablesText = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand NewCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }

    partial void OnSelectedProfileChanged(EditableProfile? value)
    {
        selectedOriginalName = value?.Name;
        EditName = value?.Name ?? string.Empty;
        EditVariablesText = value is null
            ? string.Empty
            : string.Join(Environment.NewLine, value.Variables.Select(e => $"{e.Key}={e.Value}"));
        ErrorMessage = string.Empty;
    }

    private void NewProfile()
    {
        SelectedProfile = null;
        ErrorMessage = string.Empty;
    }

    private async Task RefreshAsync()
    {
        await profileService.LoadAllAsync().ConfigureAwait(false);
        ReloadFromService();
    }

    private async Task SaveAsync()
    {
        var name = EditName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ErrorMessage = "Profile name is required.";
            return;
        }

        var clash = profileService.Profiles.Any(p =>
            !string.Equals(p.Name, selectedOriginalName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (clash)
        {
            ErrorMessage = $"A profile named '{name}' already exists.";
            return;
        }

        await profileService.SaveAsync(new EnvProfile
        {
            Name = name,
            Variables = ParseVariablesText(EditVariablesText)
        }).ConfigureAwait(false);

        await RefreshAsync().ConfigureAwait(false);
        SelectedProfile = Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        ErrorMessage = string.Empty;
    }

    private async Task DeleteAsync()
    {
        if (SelectedProfile is null)
            return;

        if (dialogs is not null &&
            !await dialogs.ConfirmAsync("Confirm Delete", $"Delete profile '{SelectedProfile.Name}'?"))
            return;

        await profileService.DeleteAsync(SelectedProfile.Name).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        SelectedProfile = null;
    }

    private void ReloadFromService()
    {
        Profiles.Clear();
        foreach (var p in profileService.Profiles)
            Profiles.Add(new EditableProfile(p.Name, p.Variables));
    }

    internal static Dictionary<string, string> ParseVariablesText(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return result;
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (trimmed.IsEmpty)
                continue;
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx <= 0)
                continue;
            var key = trimmed[..eqIdx].Trim().ToString();
            var value = trimmed[(eqIdx + 1)..].Trim().ToString();
            if (key.Length == 0 || key.Contains('\0') || value.Contains('\0'))
                continue;
            result[key] = value;
        }
        return result;
    }
}
