using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;

using CommandPrompts.App.Avalonia.ViewModels;
using CommandPrompts.App.Avalonia.Views;
using CommandPrompts.Core.Models;

using NUnit.Framework;

namespace CommandPrompts.App.Avalonia.Tests;

public class ViewInstantiationTests
{
    [AvaloniaTest]
    public void MainWindowInstantiatesAndBindsWithoutFailure()
    {
        var window = new MainWindow();
        window.Show();

        if (window.DataContext is not MainViewModel vm)
        {
            Assert.Fail("MainWindow.DataContext must be a MainViewModel");
            return;
        }

        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.Sessions, Is.Not.Null);
    }

    [AvaloniaTest]
    public async Task SessionViewMeasureArrangeForcesBindingsWithoutCastErrors()
    {
        var session = new CommandPrompts.Core.Models.Session { Name = "HeadlessTest" };
        var executor = new CommandPrompts.Core.Services.ProcessExecutor();
        using var repository = new CommandPrompts.Core.Services.SessionRepository();

        var vm = new SessionViewModel(session, executor, repository);

        var view = new SessionView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        // Pump the dispatcher until the ItemsSource binding resolves
        // (fact 714: single InvokeAsync/RunJobs is insufficient — await real
        // dispatcher round-trips with a deadline, per SessionPersistenceTests).
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (view.OutputListBox.ItemsSource is null && DateTime.UtcNow < deadline)
            await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.That(view.OutputListBox, Is.Not.Null);
        Assert.That(view.OutputListBox.ItemsSource, Is.SameAs(vm.OutputLines));

        window.Close();
        await Task.CompletedTask;
    }

    [AvaloniaTest]
    public async Task ProfileEditorDialogMeasureArrangeBindsWithoutErrors()
    {
        var service = new CommandPrompts.Core.Services.EnvProfileService(
            Path.Combine(Path.GetTempPath(), "CPProfileDlg" + Guid.NewGuid() + ".json"));
        await service.SaveAsync(new EnvProfile
        {
            Name = "Dlg",
            Variables = new() { ["K"] = "v" }
        });

        var vm = new ProfileEditorViewModel(service);
        var dialog = new Services.ProfileEditorDialog(vm);
        dialog.Show();

        dialog.Measure(new Size(640, 420));
        dialog.Arrange(new Rect(0, 0, 640, 420));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (dialog.DataContext is null && DateTime.UtcNow < deadline)
            await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.That(dialog.DataContext, Is.SameAs(vm));

        dialog.Close();
        await Task.CompletedTask;
    }

    [AvaloniaTest]
    public async Task SessionViewModelReplaysPersistedExecutionHistoryIntoOutputLines()
    {
        // WPF-parity regression: output history must survive restart. A session
        // loaded from disk carries ExecutionHistory records; the VM constructor
        // replays them into OutputLines exactly like the WPF LoadHistoricalOutput.
        var session = new CommandPrompts.Core.Models.Session { Name = "HistoryReplay" };
        var first = DateTimeOffset.UtcNow.AddMinutes(-10);
        var second = DateTimeOffset.UtcNow.AddMinutes(-5);

        var run1 = new CommandPrompts.Core.Models.ExecutionRecord
        {
            StartedAt = first,
            FinishedAt = first.AddSeconds(2),
            ExitCode = 0,
            Output =
            {
                new CommandPrompts.Core.Models.OutputEntry { Timestamp = first, Kind = CommandPrompts.Core.Models.OutputKind.System, Text = ">> Starting: cmd.exe" },
                new CommandPrompts.Core.Models.OutputEntry { Timestamp = first.AddSeconds(1), Kind = CommandPrompts.Core.Models.OutputKind.StandardOutput, Text = "hello from run 1" }
            }
        };
        var run2 = new CommandPrompts.Core.Models.ExecutionRecord
        {
            StartedAt = second,
            FinishedAt = second.AddSeconds(1),
            ExitCode = 0,
            Output =
            {
                new CommandPrompts.Core.Models.OutputEntry { Timestamp = second, Kind = CommandPrompts.Core.Models.OutputKind.StandardOutput, Text = "hello from run 2" }
            }
        };
        session.ExecutionHistory.Add(run1);
        session.ExecutionHistory.Add(run2);

        var executor = new CommandPrompts.Core.Services.ProcessExecutor();
        using var repository = new CommandPrompts.Core.Services.SessionRepository();
        var vm = new SessionViewModel(session, executor, repository);

        Assert.That(vm.OutputLines, Has.Count.EqualTo(3),
            "All persisted execution output lines must be replayed on construction");
        Assert.That(vm.OutputLines[0].Text, Is.EqualTo(">> Starting: cmd.exe"));
        Assert.That(vm.OutputLines[1].Text, Is.EqualTo("hello from run 1"));
        Assert.That(vm.OutputLines[2].Text, Is.EqualTo("hello from run 2"));
        await Task.CompletedTask;
    }

    [AvaloniaTest]
    public async Task TerminateLongRunningReturnsToIdleWithTerminatedStatus()
    {
        var session = new CommandPrompts.Core.Models.Session
        {
            Name = "TerminateTest",
            Command = "cmd.exe",
            Arguments = "/c ping -t 127.0.0.1"
        };

        var executor = new CommandPrompts.Core.Services.ProcessExecutor();
        using var repository = new CommandPrompts.Core.Services.SessionRepository();
        var vm = new SessionViewModel(session, executor, repository);

        vm.RunCommand.Execute(null);

        // Wait until running (deadline-pumped, headless-NUnit style).
        var startDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!vm.IsRunning && DateTime.UtcNow < startDeadline)
            Dispatcher.UIThread.RunJobs();
        Assert.That(vm.IsRunning, Is.True, "Process must be running before terminate");

        vm.TerminateCommand.Execute(null);

        // Must unwind to Idle even if the process tree resists the kill —
        // the wait is cancellation-linked, never stuck at "Terminating...".
        var stopDeadline = DateTime.UtcNow.AddSeconds(15);
        while (vm.RunState != SessionRunState.Idle && DateTime.UtcNow < stopDeadline)
            Dispatcher.UIThread.RunJobs();

        Assert.That(vm.RunState, Is.EqualTo(SessionRunState.Idle));
        Assert.That(vm.StatusText, Is.EqualTo("Terminated"));
        await Task.CompletedTask;
    }

    [AvaloniaTest]
    public async Task RunAsyncExecutesEchoAndCapturesExitCode()
    {
        var session = new CommandPrompts.Core.Models.Session
        {
            Name = "HeadlessRun",
            Command = "cmd.exe",
            Arguments = "/c echo HelloFromHeadless"
        };

        var executor = new CommandPrompts.Core.Services.ProcessExecutor();
        using var repository = new CommandPrompts.Core.Services.SessionRepository();
        var vm = new SessionViewModel(session, executor, repository);

        vm.RunCommand.Execute(null);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (vm.RunState != SessionRunState.Idle && DateTime.UtcNow < deadline)
            Dispatcher.UIThread.RunJobs();

        Assert.That(vm.RunState, Is.EqualTo(SessionRunState.Idle));
        Assert.That(session.ExecutionHistory, Has.Count.EqualTo(1));
        Assert.That(session.ExecutionHistory[0].ExitCode, Is.EqualTo(0));
        await Task.CompletedTask;
    }
}
