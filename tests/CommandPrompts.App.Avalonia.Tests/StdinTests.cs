using Avalonia.Headless.NUnit;

using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

using NUnit.Framework;

namespace CommandPrompts.App.Avalonia.Tests;

public class StdinTests
{
    [AvaloniaTest]
    public async Task ExecuteAsyncOnStdinReadyCallbackInvoked()
    {
        var session = new Session
        {
            Name = "StdinTest",
            Command = "cmd.exe",
            Arguments = "/c echo done"
        };
        var executor = new ProcessExecutor();
        var stdinReadyCalled = false;

        await executor.ExecuteAsync(session, _ => { }, stdinWriter =>
        {
            stdinReadyCalled = true;
        });

        Assert.That(stdinReadyCalled, Is.True,
            "onStdinReady callback must be invoked when stdin is redirected");
    }

    [AvaloniaTest]
    public async Task ExecuteAsyncStdinWriteEchoesBack()
    {
        var session = new Session
        {
            Name = "StdinEcho",
            Command = "cmd.exe",
            Arguments = "/c findstr ."
        };
        var executor = new ProcessExecutor();
        var stdinTcs = new TaskCompletionSource<TextWriter>();
        var outputLines = new List<string>();

        var runTask = Task.Run(async () =>
            await executor.ExecuteAsync(session, line =>
            {
                outputLines.Add(line.Text);
            }, stdinWriter =>
            {
                stdinTcs.TrySetResult(stdinWriter);
            }));

        var stdinWriter = await stdinTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stdinWriter.WriteLineAsync("hello");
        await stdinWriter.FlushAsync();
        stdinWriter.Close();

        var exitCode = await runTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(outputLines, Does.Contain("hello"));
    }
}
