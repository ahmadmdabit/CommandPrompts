using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using CommandPrompts.Core.Abstractions;
using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

namespace CommandPrompts.App.ViewModels;

public sealed class SessionViewModel : ObservableObject, IDisposable
{
    private const int MaxDisplayedOutputLines = 10000;
    private const int MaxStoredOutputLines = 10000;
    private const int MaxExecutionHistoryEntries = 50;

    private readonly IProcessExecutor executor;
    private readonly ISessionRepository repository;
    private readonly DebouncedAction saveDebounce;
    private readonly Lock executionOutputLock = new();
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

        LoadHistoricalOutput();

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

    private void LoadHistoricalOutput()
    {
        foreach (var exec in Session.ExecutionHistory)
        {
            foreach (var entry in exec.Output)
            {
                OutputLines.Add(new OutputLineViewModel(entry.Kind, entry.Text, entry.Timestamp));
            }
        }
    }

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

        AddExecutionOutput(currentExecution, OutputKind.System,
            $"▶ Starting: {Command} {Arguments}");

        try
        {
            var exitCode = await executor.ExecuteAsync(Session, line =>
            {
                var entry = new OutputEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Kind = line.Kind,
                    Text = line.Text
                };

                lock (executionOutputLock)
                {
                    currentExecution?.Output.Add(entry);
                    TrimExecutionOutput(currentExecution);
                }

                dispatcher.BeginInvoke(() =>
                {
                    OutputLines.Add(new OutputLineViewModel(line.Kind, line.Text, entry.Timestamp));
                    TrimDisplayedOutput();
                }, DispatcherPriority.Background);

            }, runCts.Token);

            currentExecution.ExitCode = exitCode;
            AddExecutionOutput(currentExecution, OutputKind.System,
                exitCode == 0
                    ? "✓ Process exited."
                    : $"✓ Process exited with code {exitCode}.");
        }
        catch (OperationCanceledException)
        {
            currentExecution.ExitCode = -1;
            AddExecutionOutput(currentExecution, OutputKind.System, "⊘ Process terminated.");
        }
        catch (Exception ex)
        {
            currentExecution.ExitCode = -1;
            AddExecutionOutput(currentExecution, OutputKind.System, $"✗ Error: {ex.Message}");
        }
        finally
        {
            currentExecution.FinishedAt = DateTimeOffset.UtcNow;

            if (!Session.ExecutionHistory.Contains(currentExecution))
            {
                Session.ExecutionHistory.Add(currentExecution);
                TrimExecutionHistory();
            }

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
        try
        {
            if (System.IO.File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                });
            }
            else
            {
                var dir = System.IO.Path.GetDirectoryName(path)!;
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = dir,
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to open session folder: {ex.Message}",
                "Open Folder Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AddExecutionOutput(ExecutionRecord? execution, OutputKind kind, string text)
    {
        var entry = new OutputEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = kind,
            Text = text
        };

        if (execution is not null)
        {
            lock (executionOutputLock)
            {
                execution.Output.Add(entry);
                TrimExecutionOutput(execution);
            }
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            OutputLines.Add(new OutputLineViewModel(kind, text, entry.Timestamp));
            TrimDisplayedOutput();
        }, DispatcherPriority.Background);
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

            var key = trimmed[..eqIdx].Trim().ToString();
            var value = trimmed[(eqIdx + 1)..].Trim().ToString();

            if (key.Length == 0 || key.Contains('\0') || value.Contains('\0'))
                continue;

            Session.EnvironmentVariables.Add(new Core.Models.EnvironmentVariable
            {
                Key = key,
                Value = value
            });
        }
    }

    private void QueueSave() => saveDebounce.Invoke();

    private void CopyAllOutput()
    {
        if (OutputLines.Count == 0)
            return;

        var text = string.Join(Environment.NewLine,
            OutputLines.Select(l => $"[{l.TimestampFormatted}] [{l.KindLabel}] {l.Text}"));

        TrySetClipboardText(text);
    }

    private void TrimDisplayedOutput()
    {
        while (OutputLines.Count > MaxDisplayedOutputLines)
            OutputLines.RemoveAt(0);
    }

    private static void TrimExecutionOutput(ExecutionRecord? execution)
    {
        if (execution is null)
            return;

        while (execution.Output.Count > MaxStoredOutputLines)
            execution.Output.RemoveAt(0);
    }

    private void TrimExecutionHistory()
    {
        while (Session.ExecutionHistory.Count > MaxExecutionHistoryEntries)
            Session.ExecutionHistory.RemoveAt(0);
    }

    private static void TrySetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException ex)
        {
            MessageBox.Show(
                $"Unable to access the clipboard: {ex.Message}",
                "Clipboard Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public void Dispose()
    {
        runCts?.Cancel();

        if (currentExecution is { } execution && !Session.ExecutionHistory.Contains(execution))
        {
            execution.FinishedAt ??= DateTimeOffset.UtcNow;
            execution.ExitCode ??= -1;
            Session.ExecutionHistory.Add(execution);
            TrimExecutionHistory();
        }

        runCts?.Dispose();
        runCts = null;

        SaveImmediately();

        saveDebounce.Dispose();
    }

    private void SaveImmediately()
    {
        try
        {
            repository.SaveAsync(Session).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save session: {ex}");
        }
    }
}
