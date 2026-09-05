using Avalonia;
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
    public void MainWindow_Instantiates_And_Binds_Without_Crash()
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
    public void SessionView_Measure_Arrange_Forces_Bindings_Without_Cast_Errors()
    {
        var session = new CommandPrompts.Core.Models.Session { Name = "HeadlessTest" };
        var executor = new CommandPrompts.Core.Services.ProcessExecutor();
        using var repository = new CommandPrompts.Core.Services.SessionRepository();

        var vm = new SessionViewModel(session, executor, repository);

        var view = new SessionView { DataContext = vm };
        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        Assert.That(view.OutputListBox, Is.Not.Null);
        Assert.That(view.OutputListBox.ItemsSource, Is.SameAs(vm.OutputLines));
    }

    [AvaloniaTest]
    public async Task SessionViewModel_Replays_Persisted_ExecutionHistory_Into_OutputLines()
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
    public async Task RunAsync_Executes_Echo_And_Captures_ExitCode()
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
