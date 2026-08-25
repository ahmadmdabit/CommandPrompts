using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using CommandPrompts.Core.Abstractions;
using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

namespace CommandPrompts.App.ViewModels;

public sealed class SessionViewModel : ObservableObject, IDisposable
{
    private readonly IProcessExecutor executor;
    private readonly ISessionRepository repository;
    private readonly DebouncedAction saveDebounce;
    private CancellationTokenSource? runCts;

    private string name = "New Session";
    private string command = "cmd.exe";
    private string arguments = "/c echo Hello World";
    private string workingDirectory = string.Empty;
    private string environmentVariablesText = string.Empty;
    private SessionRunState runState;
    private ExecutionRecord? currentExecution;
    private string statusText = "Idle";

    public SessionViewModel(Session session, IProcessExecutor executor, ISessionRepository repository)
    {
        Session = session;
        this.executor = executor;
        this.repository = repository;
        name = session.Name;
        command = session.Command;
        arguments = session.Arguments;
        workingDirectory = session.WorkingDirectory;
        environmentVariablesText = string.Join(Environment.NewLine,
            session.EnvironmentVariables.Select(e => $"{e.Key}={e.Value}"));

        saveDebounce = new DebouncedAction(
            ct => this.repository.SaveAsync(Session, ct),
            TimeSpan.FromMilliseconds(500));

        // Load historical output
        foreach (var exec in session.ExecutionHistory)
        {
            foreach (var entry in exec.Output)
            {
                OutputLines.Add(new OutputLineViewModel(entry.Kind, entry.Text, entry.Timestamp));
            }
        }

        RunCommand = new AsyncRelayCommand(_ => RunAsync(), _ => RunState == SessionRunState.Idle);
        TerminateCommand = new RelayCommand(_ => Terminate(), _ => RunState == SessionRunState.Running);
        ClearOutputCommand = new RelayCommand(_ => ClearOutput());
        OpenSessionFolderCommand = new RelayCommand(_ => OpenSessionFolder());
        CopyAllCommand = new RelayCommand(_ => CopyAllOutput());
    }

    public Session Session { get; }
    public Guid Id => Session.Id;

    public ObservableCollection<OutputLineViewModel> OutputLines { get; } = [];

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value))
            {
                Session.Name = value;
                QueueSave();
            }
        }
    }

    public string Command
    {
        get => command;
        set
        {
            if (SetProperty(ref command, value))
            {
                Session.Command = value;
                QueueSave();
            }
        }
    }

    public string Arguments
    {
        get => arguments;
        set
        {
            if (SetProperty(ref arguments, value))
            {
                Session.Arguments = value;
                QueueSave();
            }
        }
    }

    public string WorkingDirectory
    {
        get => workingDirectory;
        set
        {
            if (SetProperty(ref workingDirectory, value))
            {
                Session.WorkingDirectory = value;
                QueueSave();
            }
        }
    }

    public string EnvironmentVariablesText
    {
        get => environmentVariablesText;
        set
        {
            if (SetProperty(ref environmentVariablesText, value))
            {
                ParseEnvironmentVariables(value);
                QueueSave();
            }
        }
    }

    public SessionRunState RunState
    {
        get => runState;
        private set
        {
            if (SetProperty(ref runState, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                StatusText = value switch
                {
                    SessionRunState.Running => "Running...",
                    SessionRunState.Terminating => "Terminating...",
                    _ => "Idle"
                };
                ((AsyncRelayCommand)RunCommand).RaiseCanExecuteChanged();
                ((RelayCommand)TerminateCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRunning => runState != SessionRunState.Idle;

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public ICommand RunCommand { get; }
    public ICommand TerminateCommand { get; }
    public ICommand ClearOutputCommand { get; }
    public ICommand OpenSessionFolderCommand { get; }
    public ICommand CopyAllCommand { get; }

    private async Task RunAsync()
    {
        if (RunState != SessionRunState.Idle)
            return;

        RunState = SessionRunState.Running;
        runCts = new CancellationTokenSource();

        currentExecution = new ExecutionRecord
        {
            StartedAt = DateTimeOffset.UtcNow
        };

        Session.LastRunAt = currentExecution.StartedAt;
        var dispatcher = Application.Current.Dispatcher;

        AppendOutput(OutputKind.System,
            $"▶ Starting: {Command} {Arguments}");

        try
        {
            await executor.ExecuteAsync(Session, line =>
            {
                var entry = new OutputEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Kind = line.Kind,
                    Text = line.Text
                };

                currentExecution.Output.Add(entry);

                dispatcher.BeginInvoke(() =>
                {
                    OutputLines.Add(new OutputLineViewModel(line.Kind, line.Text, entry.Timestamp));

                    // Cap displayed output at 10000 lines for UI performance
                    while (OutputLines.Count > 10000)
                        OutputLines.RemoveAt(0);
                }, DispatcherPriority.Background);

            }, runCts.Token);

            currentExecution.ExitCode = 0;
            AppendOutput(OutputKind.System, "✓ Process exited.");
        }
        catch (OperationCanceledException)
        {
            currentExecution.ExitCode = -1;
            AppendOutput(OutputKind.System, "⊘ Process terminated.");
        }
        catch (Exception ex)
        {
            currentExecution.ExitCode = -1;
            AppendOutput(OutputKind.System, $"✗ Error: {ex.Message}");
        }
        finally
        {
            currentExecution.FinishedAt = DateTimeOffset.UtcNow;
            Session.ExecutionHistory.Add(currentExecution);
            currentExecution = null;

            runCts?.Dispose();
            runCts = null;
            RunState = SessionRunState.Idle;

            QueueSave();
        }
    }

    private void Terminate()
    {
        if (RunState != SessionRunState.Running)
            return;

        RunState = SessionRunState.Terminating;
        runCts?.Cancel();
    }

    private void ClearOutput()
    {
        OutputLines.Clear();
    }

    private void OpenSessionFolder()
    {
        var path = repository.GetSessionFilePath(Session.Id);
        if (System.IO.File.Exists(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        else
        {
            var dir = System.IO.Path.GetDirectoryName(path)!;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = dir,
                UseShellExecute = true
            });
        }
    }

    private void AppendOutput(OutputKind kind, string text)
    {
        var ts = DateTimeOffset.UtcNow;
        Application.Current.Dispatcher.BeginInvoke(() =>
            OutputLines.Add(new OutputLineViewModel(kind, text, ts)), DispatcherPriority.Background);
    }

    private void ParseEnvironmentVariables(string text)
    {
        Session.EnvironmentVariables.Clear();
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (trimmed.IsEmpty)
                continue;

            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx <= 0)
                continue;

            Session.EnvironmentVariables.Add(new Core.Models.EnvironmentVariable
            {
                Key = trimmed[..eqIdx].Trim().ToString(),
                Value = trimmed[(eqIdx + 1)..].Trim().ToString()
            });
        }
    }

    private void QueueSave() => saveDebounce.Invoke();

    private void CopyAllOutput()
    {
        var text = string.Join(Environment.NewLine,
            OutputLines.Select(l => $"[{l.TimestampFormatted}] [{l.KindLabel}] {l.Text}"));
        Clipboard.SetText(text);
    }

    public void Dispose()
    {
        runCts?.Cancel();
        runCts?.Dispose();
        saveDebounce.Dispose();
    }
}
