using Avalonia.Headless.NUnit;

using CommandPrompts.App.Avalonia.ViewModels;
using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

using NUnit.Framework;

namespace CommandPrompts.App.Avalonia.Tests;

public class SessionArgumentsTests
{
    [Test]
    public async Task BuildArgumentsSingleLineWithoutWhitespaceIsUnchanged()
    {
        var session = new Session { Name = "Args", Arguments = "/c" };

        Assert.That(session.BuildArguments(), Is.EqualTo("/c"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task BuildArgumentsSingleLineWithSpacesPassesThroughVerbatim()
    {
        var session = new Session { Name = "Args", Arguments = "echo done" };

        // R1: a single line (no newline) is a legacy raw command line — passed
        // through verbatim, not re-quoted.
        Assert.That(session.BuildArguments(), Is.EqualTo("echo done"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task BuildArgumentsJoinsLinesSplittingUnquotedLinesOnWhitespace()
    {
        var session = new Session { Name = "Args", Arguments = "/c\necho Hello World" };

        // Unquoted lines are split on whitespace; quoted lines stay whole.
        Assert.That(session.BuildArguments(), Is.EqualTo("/c echo Hello World"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task BuildArgumentsDropsBlankLinesAndSplitsUnquoted()
    {
        var session = new Session { Name = "Args", Arguments = "\n/c\n\n   \necho done\n" };

        Assert.That(session.BuildArguments(), Is.EqualTo("/c echo done"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task BuildArgumentsEmptyYieldsEmpty()
    {
        var session = new Session { Name = "Args", Arguments = string.Empty };

        Assert.That(session.BuildArguments(), Is.EqualTo(string.Empty));
        await Task.CompletedTask;
    }

    [Test]
    public async Task BuildArgumentsSingleLineWithEmbeddedQuotesPassesThroughVerbatim()
    {
        var session = new Session { Name = "Args", Arguments = "say \"hi\" now" };

        // R1: a single line is a legacy raw command line — embedded quotes are
        // preserved as-is, not escaped again.
        Assert.That(session.BuildArguments(), Is.EqualTo("say \"hi\" now"));
        await Task.CompletedTask;
    }

    [AvaloniaTest]
    public async Task SettingArgumentsStoresRawInputWithoutNormalization()
    {
        var session = new Session { Name = "NormalizeTest" };
        var executor = new ProcessExecutor();
        using var repository = new SessionRepository();
        var vm = new SessionViewModel(session, executor, repository);

        vm.Arguments = "\n  /c  \n\necho done\n";

        // H2: raw user input is stored verbatim; normalization happens at
        // consumption time (BuildArguments/GetProcessArguments).
        Assert.That(session.Arguments, Is.EqualTo("\n  /c  \n\necho done\n"));
        await Task.CompletedTask;
    }

    [AvaloniaTest]
    public async Task ExecuteAsyncJoinsMultiLineArgumentsIntoSingleCommandLine()
    {
        var session = new Session
        {
            Name = "MultiLineArgs",
            Command = "cmd.exe",
            Arguments = "/c\necho HelloMultiline"
        };
        var executor = new ProcessExecutor();
        var outputLines = new List<string>();

        var exitCode = await executor.ExecuteAsync(session, line => outputLines.Add(line.Text));

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(outputLines, Does.Contain("HelloMultiline"),
            "each argument line must survive quoting and reach the process");
        await Task.CompletedTask;
    }

    [Test]
    public async Task GetProcessArgumentsQuotedLineBecomesSingleArgumentQuotesStripped()
    {
        var session = new Session
        {
            Name = "Quoted",
            Arguments = "say" + Environment.NewLine + "\"hello world\""
        };

        // Quoted line → one argument, surrounding quotes removed.
        var args = session.GetProcessArguments().ToList();
        Assert.That(args, Is.EqualTo(new[] { "say", "hello world" }));
        await Task.CompletedTask;
    }

    [Test]
    public async Task GetProcessArgumentsUnquotedLineSplitsOnWhitespace()
    {
        var session = new Session { Name = "Args", Arguments = "-i fabogiolmcjaiooainieffjkdkcdahho" };

        var args = session.GetProcessArguments().ToList();
        Assert.That(args, Is.EqualTo(new[] { "-i", "fabogiolmcjaiooainieffjkdkcdahho" }));
        await Task.CompletedTask;
    }

    [Test]
    public async Task GetProcessArgumentsCase01MirrorsRequirement()
    {
        var session = new Session
        {
            Name = "Case01",
            Arguments = "/c" + Environment.NewLine +
                "node" + Environment.NewLine +
                @"D:\engamd89-dev\js-ts\gs-pro\cli\dist\cli.js" + Environment.NewLine +
                "-i fabogiolmcjaiooainieffjkdkcdahho" + Environment.NewLine +
                "--execution-timeout 900" + Environment.NewLine +
                "--pretty" + Environment.NewLine +
                "-f json" + Environment.NewLine +
                "--out search-result.json" + Environment.NewLine +
                "\"LLMS in 2026-10\""
        };

        var args = session.GetProcessArguments().ToList();
        Assert.That(args, Is.EqualTo(new[]
        {
            "/c", "node",
            @"D:\engamd89-dev\js-ts\gs-pro\cli\dist\cli.js",
            "-i", "fabogiolmcjaiooainieffjkdkcdahho",
            "--execution-timeout", "900",
            "--pretty",
            "-f", "json",
            "--out", "search-result.json",
            "LLMS in 2026-10"
        }));
        await Task.CompletedTask;
    }

    [Test]
    public async Task GetProcessArgumentsCase02MirrorsRequirement()
    {
        var session = new Session
        {
            Name = "Case02",
            Arguments = "/c" + Environment.NewLine +
                "\"D:\\engamd89-dev\\folder name\\cli\\app.exe\"" + Environment.NewLine +
                "--arg1" + Environment.NewLine +
                "--arg2" + Environment.NewLine +
                "--out result.md"
        };

        var args = session.GetProcessArguments().ToList();
        Assert.That(args, Is.EqualTo(new[]
        {
            "/c",
            @"D:\engamd89-dev\folder name\cli\app.exe",
            "--arg1", "--arg2",
            "--out", "result.md"
        }));
        await Task.CompletedTask;
    }

    [AvaloniaTest]
    public async Task ExecuteAsyncCase01RunsThroughCmdExe()
    {
        var session = new Session
        {
            Name = "Case01Run",
            Command = "cmd.exe",
            Arguments = "/c" + Environment.NewLine +
                "echo" + Environment.NewLine +
                "HelloCase01"
        };
        var executor = new ProcessExecutor();
        var outputLines = new List<string>();

        var exitCode = await executor.ExecuteAsync(session, line => outputLines.Add(line.Text));

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(outputLines, Does.Contain("HelloCase01"));
        await Task.CompletedTask;
    }

    [AvaloniaTest]
    public async Task ClearSelectionCommandClearsSelectedOutputLines()
    {
        var session = new Session { Name = "ClearSel" };
        var execution = new ExecutionRecord
        {
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            ExitCode = 0,
            Output =
            {
                new OutputEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Kind = OutputKind.StandardOutput,
                    Text = "selectable line"
                }
            }
        };
        session.ExecutionHistory.Add(execution);
        var executor = new ProcessExecutor();
        using var repository = new SessionRepository();
        var vm = new SessionViewModel(session, executor, repository);

        Assert.That(vm.OutputLines.Count, Is.GreaterThan(0));
        vm.SelectedOutputLines.Add(vm.OutputLines[0]);
        Assert.That(vm.SelectedOutputLines.Count, Is.EqualTo(1));

        vm.ClearSelectionCommand.Execute(null);

        Assert.That(vm.SelectedOutputLines.Count, Is.EqualTo(0));
        await Task.CompletedTask;
    }
}
