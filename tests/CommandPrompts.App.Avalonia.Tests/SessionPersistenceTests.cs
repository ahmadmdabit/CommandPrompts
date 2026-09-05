using Avalonia.Headless.NUnit;
using Avalonia.Threading;

using CommandPrompts.App.Avalonia.ViewModels;

using NUnit.Framework;

namespace CommandPrompts.App.Avalonia.Tests;

/// <summary>
/// Regression tests for the "sessions do not reappear after restart" bug.
/// NOTE: Environment.SetEnvironmentVariable("LOCALAPPDATA") does NOT influence
/// Environment.GetFolderPath on Windows (resolved from the profile at process
/// start), so these run against the real History directory with count-derived
/// assertions instead of a seeded sandbox.
/// Tests await the load command end-to-end (headless NUnit pumps the dispatcher
/// across awaits), so results applied via Dispatcher.UIThread.InvokeAsync are
/// deterministically observed — no RunJobs-timing race.
/// </summary>
public class SessionPersistenceTests
{
    private static int RealHistoryFileCount()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CommandPrompts", "History");

        if (!Directory.Exists(dir))
            return 0;

        return Directory.GetFiles(dir, "*.json").Length;
    }

    private static void AssertLoadHealthy(MainViewModel vm, string because)
    {
        // LoadError FIRST: an empty session list caused by a real load failure must
        // be reported as such, never masquerade as "no persisted sessions".
        Assert.That(vm.LoadError, Is.Empty,
            $"Load failed during: {because}. Error: {vm.LoadError}");
    }

    [AvaloniaTest]
    public async Task LoadSessionsAsync_Populates_Sessions_From_Disk()
    {
        var expectedFiles = RealHistoryFileCount();

        var vm = new MainViewModel();

        await vm.LoadedCommand.ExecuteAsync(null);

        if (expectedFiles == 0)
        {
            // Nothing persisted yet — the meaningful assertion is no error + no fatal.
            AssertLoadHealthy(vm, "empty-history load");
            return;
        }

        AssertLoadHealthy(vm, "populated load");
        // Corrupt/concurrently-written files may be legitimately skipped by the
        // repository, so assert range rather than exact equality.
        Assert.That(vm.Sessions.Count, Is.GreaterThanOrEqualTo(1),
            "Persisted sessions must appear after load");
        Assert.That(vm.Sessions.Count, Is.LessThanOrEqualTo(expectedFiles));
        Assert.That(vm.SelectedSession, Is.Not.Null);
    }

    [AvaloniaTest]
    public async Task LoadSessionsAsync_Is_Idempotent_NoDuplicates()
    {
        var vm = new MainViewModel();

        await vm.LoadedCommand.ExecuteAsync(null);
        var firstCount = vm.Sessions.Count;

        // Second invocation must not duplicate (hasLoaded guard) — regression guard
        // for the retry logic added with the persistence fix.
        await vm.LoadedCommand.ExecuteAsync(null);

        AssertLoadHealthy(vm, "repeated load");
        Assert.That(vm.Sessions.Count, Is.EqualTo(firstCount),
            "Repeated loads must not duplicate sessions");
    }

    [AvaloniaTest]
    public async Task MainWindow_OnOpened_Loads_Persisted_Sessions()
    {
        var expectedFiles = RealHistoryFileCount();

        var window = new CommandPrompts.App.Avalonia.Views.MainWindow();
        window.Show();

        // OnOpened fires the initial load as fire-and-forget; wait for its guard
        // flags to settle instead of guessing a RunJobs count.
        if (window.DataContext is not MainViewModel vm)
        {
            Assert.Fail("MainWindow.DataContext must be a MainViewModel");
            return;
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!vm.IsLoadingCompleted && DateTime.UtcNow < deadline)
            await Dispatcher.UIThread.InvokeAsync(() => { });

        if (expectedFiles == 0)
        {
            AssertLoadHealthy(vm, "empty-history startup load");
            return;
        }

        AssertLoadHealthy(vm, "startup load");
        Assert.That(vm.Sessions.Count, Is.GreaterThanOrEqualTo(1),
            "Persisted sessions must be loaded automatically at startup (OnOpened path)");
    }
}
