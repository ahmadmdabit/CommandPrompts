using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommandPrompts.Core.Abstractions;
using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

namespace CommandPrompts.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IProcessExecutor executor;
    private readonly ISessionRepository repository;
    private SessionViewModel? selectedSession;
    private bool isLoading;

    public MainViewModel()
    {
        executor = new ProcessExecutor();
        repository = new SessionRepository();

        NewSessionCommand = new RelayCommand(_ => CreateNewSession());
        DeleteSessionCommand = new AsyncRelayCommand(
            _ => DeleteSelectedSessionAsync(),
            _ => SelectedSession is not null);
        LoadedCommand = new AsyncRelayCommand(_ => LoadSessionsAsync());
    }

    public ObservableCollection<SessionViewModel> Sessions { get; } = [];

    public SessionViewModel? SelectedSession
    {
        get => selectedSession;
        set
        {
            if (SetProperty(ref selectedSession, value))
                ((AsyncRelayCommand)DeleteSessionCommand).RaiseCanExecuteChanged();
        }
    }

    public bool IsLoading
    {
        get => isLoading;
        private set => SetProperty(ref isLoading, value);
    }

    public ICommand NewSessionCommand { get; }
    public ICommand DeleteSessionCommand { get; }
    public ICommand LoadedCommand { get; }

    private async Task LoadSessionsAsync()
    {
        IsLoading = true;
        try
        {
            var sessions = await repository.LoadAllAsync();
            foreach (var s in sessions)
            {
                Sessions.Add(new SessionViewModel(s, executor, repository));
            }

            if (Sessions.Count > 0)
                SelectedSession = Sessions[0];
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void CreateNewSession()
    {
        var session = new Session
        {
            Name = $"Session {Sessions.Count + 1}",
            Command = "cmd.exe",
            Arguments = "/c echo Hello World"
        };

        var vm = new SessionViewModel(session, executor, repository);
        Sessions.Add(vm);
        SelectedSession = vm;

        // Persist immediately
        _ = repository.SaveAsync(session);
    }

    private async Task DeleteSelectedSessionAsync()
    {
        if (SelectedSession is null)
            return;

        var session = SelectedSession;

        var result = MessageBox.Show(
            $"Delete session '{session.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        session.Dispose();
        Sessions.Remove(session);
        await repository.DeleteAsync(session.Id);

        SelectedSession = Sessions.Count > 0 ? Sessions[0] : null;
    }

    public void Dispose()
    {
        foreach (var s in Sessions)
            s.Dispose();
    }
}
