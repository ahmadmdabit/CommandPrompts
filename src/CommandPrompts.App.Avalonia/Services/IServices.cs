using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Input;

using CommandPrompts.Core.Services;

namespace CommandPrompts.App.Avalonia.Services;

/// <summary>Confirmation dialog abstraction (docs pattern: ShowDialog&lt;bool?&gt;(owner)).</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    void Inform(string title, string message);
    Task ManageProfilesAsync(EnvProfileService profiles);
}

/// <summary>Clipboard abstraction — async end-to-end per the v12 API.</summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
}

/// <summary>OS launcher abstraction so VMs never call Process.Start directly.</summary>
public interface IProcessLauncher
{
    void OpenInExplorer(string path);
}

public sealed class DialogService(Window owner) : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ConfirmDialog(title, message);
        return await dialog.ShowDialog<bool?>(owner) == true;
    }

    public void Inform(string title, string message)
    {
        var dialog = new ConfirmDialog(title, message, cancelVisible: false);
        _ = dialog.ShowDialog(owner);
    }

    public async Task ManageProfilesAsync(EnvProfileService profiles)
    {
        var viewModel = new ViewModels.ProfileEditorViewModel(profiles, this);
        var dialog = new ProfileEditorDialog(viewModel);
        await dialog.ShowDialog(owner);
    }
}

public sealed class ClipboardService(Window owner) : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard is null || string.IsNullOrEmpty(text))
            return;

        // Avalonia 12 clipboard API: DataTransfer + DataTransferItem (IDataObject removed).
        var item = DataTransferItem.CreateText(text);
        using var data = new DataTransfer();
        data.Add(item);

        await clipboard.SetDataAsync(data);
    }
}

public sealed class ProcessLauncherService : IProcessLauncher
{
    public void OpenInExplorer(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\""));
    }
}
