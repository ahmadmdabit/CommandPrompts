using System.Collections.ObjectModel;

using Avalonia.Threading;

using CommandPrompts.Core.Abstractions;
using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CommandPrompts.App.Avalonia.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IProcessExecutor executor;
    private readonly ISessionRepository repository;
    private Services.IDialogService? dialogService;
    private bool hasLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSessionCommand))]
    private SessionViewModel? selectedSession;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string loadError = string.Empty;

    /// <summary>True once a load attempt has fully settled (success or failure).</summary>
    [ObservableProperty]
    private bool isLoadingCompleted;

    public MainViewModel()
    {
        executor = new ProcessExecutor();
        repository = new SessionRepository();

        NewSessionCommand = new AsyncRelayCommand(CreateNewSessionAsync);
        DeleteSessionCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync, CanDeleteSession);
        LoadedCommand = new AsyncRelayCommand(LoadSessionsAsync);
        SelectSessionCommand = new RelayCommand<SessionViewModel>(SelectSession);
    }

    /// <summary>View-layer services that need an owner window; attached after construction.</summary>
    public void AttachServices(Services.IDialogService dialogs, Services.IClipboardService clipboard,
        Services.IProcessLauncher? processLauncher = null)
    {
        dialogService = dialogs;
        foreach (var s in Sessions)
            s.AttachServices(dialogs, clipboard, processLauncher);

        Sessions.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (SessionViewModel s in e.NewItems)
                    s.AttachServices(dialogs, clipboard, processLauncher);
        };
    }

    public ObservableCollection<SessionViewModel> Sessions { get; } = [];

    public IAsyncRelayCommand NewSessionCommand { get; }
    public IAsyncRelayCommand DeleteSessionCommand { get; }
    public IAsyncRelayCommand LoadedCommand { get; }
    public IRelayCommand<SessionViewModel> SelectSessionCommand { get; }

    private bool CanDeleteSession() => SelectedSession is not null;

    private void SelectSession(SessionViewModel session)
    {
        SelectedSession = session;
    }

    partial void OnSelectedSessionChanged(SessionViewModel? value)
    {
        foreach (var s in Sessions)
            s.IsSelected = (s == value);
        DeleteSessionCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadSessionsAsync()
    {
        // Guard against double-load, but only AFTER a successful load — a failed
        // attempt must remain retryable (root cause of the "sessions vanish" bug).
        if (hasLoaded || IsLoading)
            return;

        IsLoading = true;
        LoadError = string.Empty;
        try
        {
            var sessions = await repository.LoadAllAsync();

            // Apply results on the UI thread: the await continuation may resume on a
            // worker thread, and ObservableCollection mutation + SelectedSession must
            // happen on the Dispatcher (app-development/threading.md — InvokeAsync).
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Sessions.Clear();
                foreach (var s in sessions)
                    Sessions.Add(new SessionViewModel(s, executor, repository));

                if (Sessions.Count > 0)
                    SelectedSession = Sessions[0];

                hasLoaded = true;
            });
        }
        catch (Exception ex)
        {
            // Surface the failure: never leave an empty list silently.
            LoadError = $"Failed to load sessions: {ex.Message}";
            System.Diagnostics.Trace.TraceError($"LoadSessionsAsync failed: {ex}");
        }
        finally
        {
            IsLoading = false;
            IsLoadingCompleted = true;
        }
    }

    private async Task CreateNewSessionAsync()
    {
        var session = new Session
        {
            Name = $"Session {Sessions.Count + 1}",
            Command = "cmd.exe",
            Arguments = "/c echo Hello World"
        };

        var vm = new SessionViewModel(session, executor, repository);
        if (dialogService is not null)
            vm.AttachServices(dialogService, null!);

        Sessions.Add(vm);
        SelectedSession = vm;

        await repository.SaveAsync(session);
    }

    private async Task DeleteSelectedSessionAsync()
    {
        if (SelectedSession is null)
            return;

        if (dialogService is null)
            return; // no dialog service yet — refuse destructive action

        var session = SelectedSession;
        if (!await dialogService.ConfirmAsync("Confirm Delete", $"Delete session '{session.Name}'?"))
            return;

        await repository.DeleteAsync(session.Id);

        session.Dispose();
        Sessions.Remove(session);

        SelectedSession = Sessions.Count > 0 ? Sessions[0] : null;
    }
}
