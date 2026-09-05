using System.Collections.ObjectModel;
using System.Diagnostics;

using Avalonia.Media;
using Avalonia.Threading;

using CommandPrompts.Core.Abstractions;
using CommandPrompts.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CommandPrompts.App.Avalonia.ViewModels;

public partial class SessionViewModel : ObservableObject, IDisposable
{
    private const int MaxDisplayedOutputLines = 10000;
    private const int MaxStoredOutputLines = 10000;
    private const int MaxExecutionHistoryEntries = 50;

    private readonly IProcessExecutor executor;
    private readonly ISessionRepository repository;
    private readonly object executionOutputLock = new();
    private CancellationTokenSource? runCts;
    private Services.IDialogService? dialogService;
    private Services.IClipboardService? clipboardService;
    private Services.IProcessLauncher? processLauncher;

    public void AttachServices(Services.IDialogService dialogs, Services.IClipboardService? clipboard,
        Services.IProcessLauncher? processLauncher = null)
    {
        dialogService = dialogs;
        clipboardService = clipboard;
        this.processLauncher = processLauncher;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(TerminateCommand))]
    private SessionRunState runState;

    [ObservableProperty]
    private string statusText = "Idle";

    [ObservableProperty]
    private string name = "New Session";

    [ObservableProperty]
    private string command = "cmd.exe";

    [ObservableProperty]
    private string arguments = "/c echo Hello World";

    [ObservableProperty]
    private string workingDirectory = string.Empty;

    [ObservableProperty]
    private string environmentVariablesText = string.Empty;

    partial void OnNameChanged(string value) { Session.Name = value; QueueSave(); }
    partial void OnCommandChanged(string value) { Session.Command = value; QueueSave(); }
    partial void OnArgumentsChanged(string value) { Session.Arguments = value; QueueSave(); }
    partial void OnWorkingDirectoryChanged(string value) { Session.WorkingDirectory = value; QueueSave(); }
    partial void OnEnvironmentVariablesTextChanged(string value)
    {
        ParseEnvironmentVariables(value);
        QueueSave();
    }

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

        RunCommand = new AsyncRelayCommand(RunAsync, () => RunState == SessionRunState.Idle);
        TerminateCommand = new RelayCommand(Terminate, () => RunState == SessionRunState.Running);
        ClearOutputCommand = new RelayCommand(ClearOutput);
        OpenSessionFolderCommand = new RelayCommand(OpenSessionFolder);
        CopyAllCommand = new AsyncRelayCommand(CopyAllOutputAsync);
        CopySelectedCommand = new AsyncRelayCommand(CopySelectedOutputAsync, HasSelection);
        CopySelectedOrAllCommand = new AsyncRelayCommand(CopySelectedOrAllAsync);

        LoadHistoricalOutput();
    }

    /// <summary>
    /// Replays previously-persisted run output (Session.ExecutionHistory) into
    /// OutputLines so history survives restarts — WPF-parity behavior
    /// (LoadHistoricalOutput). Must run on the UI thread: callers are the
    /// constructor and the UI-thread-marshaled load path.
    /// </summary>
    private void LoadHistoricalOutput()
    {
        foreach (var exec in Session.ExecutionHistory)
        {
            foreach (var entry in exec.Output)
                OutputLines.Add(new OutputLineViewModel(entry.Kind, entry.Text, entry.Timestamp));
        }
    }

    public Session Session { get; }
    public Guid Id => Session.Id;

    public ObservableCollection<OutputLineViewModel> OutputLines { get; } = [];

    /// <summary>
    /// Currently selected output lines (multi-select; right-click and Ctrl+click
    /// update it before the context menu / key gesture fires).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<OutputLineViewModel> selectedOutputLines = [];

    /// <summary>True when this session is the active selection in the parent list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedBackground))]
    private bool isSelected;

    public IBrush SelectedBackground => IsSelected
        ? new SolidColorBrush(Color.Parse("#60993E"))
        : new SolidColorBrush(Colors.Transparent);

    public bool IsRunning => RunState != SessionRunState.Idle;

    partial void OnRunStateChanged(SessionRunState value)
    {
        OnPropertyChanged(nameof(IsRunning));
        StatusText = value switch
        {
            SessionRunState.Running => "Running...",
            SessionRunState.Terminating => "Terminating...",
            _ => "Idle"
        };
    }

    public IAsyncRelayCommand RunCommand { get; }
    public IRelayCommand TerminateCommand { get; }
    public IRelayCommand ClearOutputCommand { get; }
    public IRelayCommand OpenSessionFolderCommand { get; }
    public IAsyncRelayCommand CopyAllCommand { get; }
    public IAsyncRelayCommand CopySelectedCommand { get; }
    public IAsyncRelayCommand CopySelectedOrAllCommand { get; }

    private async Task RunAsync()
    {
        if (RunState != SessionRunState.Idle)
            return;

        RunState = SessionRunState.Running;
        runCts = new CancellationTokenSource();

        var execution = new ExecutionRecord
        {
            StartedAt = DateTimeOffset.UtcNow
        };

        Session.LastRunAt = execution.StartedAt;

        AddExecutionOutput(execution, OutputKind.System,
            $">> Starting: {Session.Command} {Session.Arguments}");

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
                    execution.Output.Add(entry);
                    TrimExecutionOutput(execution);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    OutputLines.Add(new OutputLineViewModel(line.Kind, line.Text, entry.Timestamp));
                    TrimDisplayedOutput();
                });

            }, runCts.Token);

            execution.ExitCode = exitCode;
            AddExecutionOutput(execution, OutputKind.System,
                exitCode == 0
                    ? "OK Process exited."
                    : $"!! Process exited with code {exitCode}.");
        }
        catch (OperationCanceledException)
        {
            execution.ExitCode = -1;
            AddExecutionOutput(execution, OutputKind.System, "XX Process terminated.");
        }
        catch (Exception ex)
        {
            execution.ExitCode = -1;
            AddExecutionOutput(execution, OutputKind.System, $"!! Error: {ex.Message}");
        }
        finally
        {
            execution.FinishedAt = DateTimeOffset.UtcNow;

            if (!Session.ExecutionHistory.Contains(execution))
            {
                Session.ExecutionHistory.Add(execution);
                TrimExecutionHistory();
            }

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
            var dir = File.Exists(path)
                ? Path.GetDirectoryName(path)!
                : path;

            if (processLauncher is not null)
            {
                processLauncher.OpenInExplorer(dir);
            }
            else
            {
                // No launcher attached (headless/tests) — refuse silently 
                Debug.WriteLine("OpenSessionFolder: no IProcessLauncher attached.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Unable to open session folder: {ex.Message}");
        }
    }

    private void AddExecutionOutput(ExecutionRecord execution, OutputKind kind, string text)
    {
        var entry = new OutputEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = kind,
            Text = text
        };

        lock (executionOutputLock)
        {
            execution.Output.Add(entry);
            TrimExecutionOutput(execution);
        }

        Dispatcher.UIThread.Post(() =>
        {
            OutputLines.Add(new OutputLineViewModel(kind, text, entry.Timestamp));
            TrimDisplayedOutput();
        });
    }

    private void QueueSave() => _ = repository.SaveAsync(Session);

    private async Task CopyAllOutputAsync()
    {
        if (OutputLines.Count == 0)
            return;

        await CopyToClipboardAsync(FormatLines(OutputLines));
    }

    private bool HasSelection() => SelectedOutputLines.Count > 0;

    /// <summary>Copies the selected output lines in display order.</summary>
    private async Task CopySelectedOutputAsync()
    {
        if (!HasSelection())
            return;

        await CopyToClipboardAsync(FormatLines(SelectedOutputLines));
    }

    /// <summary>Ctrl+C behavior: selected lines when a selection exists, otherwise the full output.</summary>
    private async Task CopySelectedOrAllAsync()
        => await (HasSelection() ? CopySelectedOutputAsync() : CopyAllOutputAsync());

    private static string FormatLines(IEnumerable<OutputLineViewModel> lines)
        => string.Join(Environment.NewLine, lines.Select(FormatLine));

    private static string FormatLine(OutputLineViewModel line)
        => $"[{line.TimestampFormatted}] [{line.KindLabel}] {line.Text}";

    private async Task CopyToClipboardAsync(string text)
    {
        if (clipboardService is null)
            return;

        try
        {
            await clipboardService.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clipboard write failed: {ex.Message}");
            dialogService?.Inform("Clipboard Error", $"Unable to copy output: {ex.Message}");
        }
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

            Session.EnvironmentVariables.Add(new EnvironmentVariable
            {
                Key = key,
                Value = value
            });
        }
    }

    private void TrimDisplayedOutput()
    {
        while (OutputLines.Count > MaxDisplayedOutputLines)
            OutputLines.RemoveAt(0);
    }

    private static void TrimExecutionOutput(ExecutionRecord execution)
    {
        while (execution.Output.Count > MaxStoredOutputLines)
            execution.Output.RemoveAt(0);
    }

    private void TrimExecutionHistory()
    {
        while (Session.ExecutionHistory.Count > MaxExecutionHistoryEntries)
            Session.ExecutionHistory.RemoveAt(0);
    }

    public void Dispose()
    {
        runCts?.Cancel();

        runCts?.Dispose();
        runCts = null;
    }
}
