using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

using Avalonia.Media;
using Avalonia.Threading;

using CommandPrompts.Core.Abstractions;
using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CommandPrompts.App.Avalonia.ViewModels;

public sealed partial class SessionViewModel : ObservableObject, IDisposable
{
    private const int MaxStoredOutputLines = 10000;
    private const int MaxExecutionHistoryEntries = 50;

    private readonly IProcessExecutor executor;
    private readonly ISessionRepository repository;
    private readonly object executionOutputLock = new();
    private CancellationTokenSource? runCts;
    private TextWriter? stdinWriter;
    private EnvProfileService? profileService;
    private Services.IDialogService? dialogService;
    private Services.IClipboardService? clipboardService;
    private Services.IProcessLauncher? processLauncher;

    // R1 fix: track user-entered vars separately from profile-derived vars so
    // switching profiles replaces (not accumulates) the profile portion.
    private Dictionary<string, string> userVars = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> profileVars = new(StringComparer.OrdinalIgnoreCase);

    // Guard so that programmatic EnvironmentVariablesText updates (from profile
    // changes) don't re-parse the merged text back as user vars.
    private bool isRebuildingEnvText;

    private static readonly Regex PasswordPromptRegex = new(
        @"(password|passphrase|enter\s+pin|credentials?\s*for)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    [ObservableProperty]
    private string? selectedProfileName;

    [ObservableProperty]
    private IReadOnlyList<EnvProfile> availableProfiles = Array.Empty<EnvProfile>();

    [ObservableProperty]
    private string inputText = string.Empty;

    [ObservableProperty]
    private bool isPasswordMode;

    partial void OnNameChanged(string value) { Session.Name = value; QueueSave(); }
    partial void OnCommandChanged(string value) { Session.Command = value; QueueSave(); }
    partial void OnArgumentsChanged(string value) { Session.Arguments = value; QueueSave(); }
    partial void OnWorkingDirectoryChanged(string value) { Session.WorkingDirectory = value; QueueSave(); }
    partial void OnEnvironmentVariablesTextChanged(string value)
    {
        // R1 fix: when the text was set programmatically by a profile change,
        // don't re-parse it back into user vars — that would defeat the
        // profile/user separation. Only sync user vars on genuine user edits.
        if (!isRebuildingEnvText)
        {
            userVars = ParseToDictionary(value);
        }
        ParseEnvironmentVariables(value);
        QueueSave();
    }

    /// <summary>
    /// Attaches the shared env-profile service (mirrors AttachServices).
    /// Does not merge on attach — direct field assignment avoids a startup rewrite.
    /// </summary>
    public void AttachProfiles(EnvProfileService service)
    {
        profileService = service;
        AvailableProfiles = service.Profiles;
        selectedProfileName = Session.EnvProfileName;
    }

    partial void OnSelectedProfileNameChanged(string? value)
    {
        Session.EnvProfileName = value;
        QueueSave();
        if (profileService is null)
            return;

        // R1 fix: rebuild profile vars from scratch (not accumulated) by
        // clearing and re-deriving from the selected profile. Then merge
        // with user vars (user wins) to produce the final text.
        profileVars.Clear();
        if (!string.IsNullOrEmpty(value))
        {
            var profile = profileService.Profiles.FirstOrDefault(p =>
                string.Equals(p.Name, value, StringComparison.OrdinalIgnoreCase));
            if (profile is not null)
            {
                foreach (var kv in profile.Variables)
                    profileVars[kv.Key] = kv.Value;
            }
        }
        RebuildEnvironmentVariablesText();
    }

    /// <summary>
    /// Rebuilds <see cref="EnvironmentVariablesText"/> from the current user vars
    /// merged over the current profile vars. User vars always win.
    /// Call after changing either set.
    /// </summary>
    private void RebuildEnvironmentVariablesText()
    {
        var effective = new Dictionary<string, string>(profileVars, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in userVars)
            effective[kv.Key] = kv.Value;
        var merged = string.Join(Environment.NewLine,
            effective.Select(e => $"{e.Key}={e.Value}"));
        if (!string.Equals(merged, EnvironmentVariablesText, StringComparison.Ordinal))
        {
            // Guard: this update is programmatic (profile change), not a user
            // edit — don't re-parse the merged text back into user vars.
            isRebuildingEnvText = true;
            EnvironmentVariablesText = merged;
            isRebuildingEnvText = false;
        }
    }

    /// <summary>
    /// Re-pushes the shared profile list (e.g. after the profile editor closes)
    /// and falls back to user-only vars when the selected profile was deleted.
    /// </summary>
    public void RefreshProfiles()
    {
        if (profileService is null)
            return;
        AvailableProfiles = profileService.Profiles;
        if (string.IsNullOrEmpty(Session.EnvProfileName))
            return;
        var stillExists = profileService.Profiles.Any(p =>
            string.Equals(p.Name, Session.EnvProfileName, StringComparison.OrdinalIgnoreCase));
        if (!stillExists)
            SelectedProfileName = null; // handler clears profile vars, rebuilds from user vars
    }

    partial void OnInputTextChanged(string value)
    {
        SubmitInputCommand.NotifyCanExecuteChanged();
        IsPasswordMode = false;
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

        // R1 fix: seed user vars from the session's stored env vars so that
        // switching profiles on a session that already has vars doesn't
        // merge the old vars back as "user" vars.
        userVars = ParseToDictionary(environmentVariablesText);
        profileVars = new(StringComparer.OrdinalIgnoreCase);

        RunCommand = new AsyncRelayCommand(RunAsync, () => RunState == SessionRunState.Idle);
        TerminateCommand = new RelayCommand(Terminate, () => RunState == SessionRunState.Running);
        ClearOutputCommand = new RelayCommand(ClearOutput);
        OpenSessionFolderCommand = new RelayCommand(OpenSessionFolder);
        CopyAllCommand = new AsyncRelayCommand(CopyAllOutputAsync);
        CopySelectedCommand = new AsyncRelayCommand(CopySelectedOutputAsync, HasSelection);
        CopySelectedOrAllCommand = new AsyncRelayCommand(CopySelectedOrAllAsync);
        SubmitInputCommand = new RelayCommand(SubmitInput, () => !string.IsNullOrEmpty(InputText));

        outputBuffer = new DiskBackedOutputBuffer(repository.GetJournal(Id));
        OutputLines = outputBuffer;
        // Sync warm-start: replay in-memory ExecutionHistory synchronously so
        // first paint + tests are deterministic (ctor runs on UI thread).
        // buffer.Add journals each line via the shared repository journal.
        warmStartedFromHistory = Session.ExecutionHistory.Count > 0;
        foreach (var exec in Session.ExecutionHistory)
        {
            foreach (var entry in exec.Output)
            {
                outputBuffer.Add(new OutputLineViewModel(entry.Kind, entry.Text, entry.Timestamp));
            }
        }

        LoadHistoricalOutput();
    }

    /// <summary>
    /// Replays previously-persisted output from the append-only journal into
    /// OutputLines so history survives restarts. Fire-and-forget async
    /// because the constructor can't await.
    /// </summary>
    private void LoadHistoricalOutput()
    {
        _ = LoadHistoricalOutputAsync();
    }

    private async Task LoadHistoricalOutputAsync()
    {
        // Warm-start already populated the buffer from ExecutionHistory;
        // journal was backfilled — skip to avoid double-add.
        if (warmStartedFromHistory)
            return;
        try
        {
            var entries = await repository.ReadOutputAsync(Id);
            if (entries.Count == 0 && Session.ExecutionHistory.Count > 0)
            {
                // Migration: existing sessions have history in ExecutionHistory but no journal yet.
                // Replay into journal so the new code path has data.
                foreach (var exec in Session.ExecutionHistory)
                {
                    foreach (var entry in exec.Output)
                    {
                        await repository.AppendOutputAsync(Id, entry);
                        outputBuffer?.Add(new OutputLineViewModel(entry.Kind, entry.Text, entry.Timestamp));
                    }
                }
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var entry in entries)
                    outputBuffer?.Add(new OutputLineViewModel(entry.Kind, entry.Text, entry.Timestamp));
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load historical output: {ex.Message}");
        }
    }

    public Session Session { get; }
    public Guid Id => Session.Id;

    public IList<OutputLineViewModel> OutputLines { get; }
    private DiskBackedOutputBuffer? outputBuffer;
    private readonly bool warmStartedFromHistory;

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
    public IRelayCommand SubmitInputCommand { get; }

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

        var terminatedByUser = false;
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

                // buffer.Add below journals via the shared repository journal (single writer).
                Dispatcher.UIThread.Post(() =>
                {
                    outputBuffer?.Add(new OutputLineViewModel(line.Kind, line.Text, entry.Timestamp));
                });

                if (PasswordPromptRegex.IsMatch(line.Text))
                    IsPasswordMode = true;

            }, stdinWriter =>
            {
                this.stdinWriter = stdinWriter;
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
            terminatedByUser = true;
            AddExecutionOutput(execution, OutputKind.System, "XX Process terminated.");
            Debug.WriteLine($"Session '{Session.Name}' terminated by user.");
            Trace.TraceInformation($"Session '{Session.Name}' terminated by user.");
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
            stdinWriter = null;
            RunState = SessionRunState.Idle;

            // Leave an explicit terminal outcome: Idle would otherwise erase
            // any trace that the run was terminated (next run overwrites).
            if (terminatedByUser)
                StatusText = "Terminated";

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

    private void SubmitInput()
    {
        if (stdinWriter is null || string.IsNullOrEmpty(InputText))
            return;
        stdinWriter.WriteLine(InputText);
        stdinWriter.Flush();
        InputText = string.Empty;
    }

    private void ClearOutput()
    {
        outputBuffer?.Clear();
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

        // buffer.Add below journals via the shared repository journal (single writer).
        Dispatcher.UIThread.Post(() =>
        {
            outputBuffer?.Add(new OutputLineViewModel(kind, text, entry.Timestamp));
        });
    }

    private void QueueSave() => _ = repository.SaveAsync(Session);

    private async Task CopyAllOutputAsync()
    {
        if (OutputLines.Count == 0)
            return;

        var allLines = new List<string>();
        for (int i = 0; i < OutputLines.Count; i++)
            allLines.Add(FormatLine(OutputLines[i]));
        await CopyToClipboardAsync(string.Join(Environment.NewLine, allLines));
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

    private static Dictionary<string, string> ParseToDictionary(string text)
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

        // C2 fix: release the cached journal's file handles so they don't leak
        // for the lifetime of the repository. The journal is recreated on next
        // access if the session is reactivated.
        repository.CloseJournal(Id);

        outputBuffer?.Dispose();
    }
}
