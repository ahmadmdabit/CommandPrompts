using System.Diagnostics;
using System.Text;

using CommandPrompts.Core.Abstractions;
using CommandPrompts.Core.Models;

namespace CommandPrompts.Core.Services;

public sealed class ProcessExecutor : IProcessExecutor
{
    public async Task<int> ExecuteAsync(
        Session session,
        Action<ProcessOutputLine> onOutput,
        Action<TextWriter>? onStdinReady = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onOutput);

        if (string.IsNullOrWhiteSpace(session.Command))
        {
            onOutput(new ProcessOutputLine(OutputKind.System, "Command is required."));
            return -1;
        }

        ct.ThrowIfCancellationRequested();
        using var process = new Process();
        var si = process.StartInfo;

        si.FileName = session.Command;
        foreach (var arg in session.GetProcessArguments())
            si.ArgumentList.Add(arg);

        if (!string.IsNullOrWhiteSpace(session.WorkingDirectory))
        {
            if (!Directory.Exists(session.WorkingDirectory))
            {
                onOutput(new ProcessOutputLine(
                    OutputKind.System,
                    $"Working directory does not exist: {session.WorkingDirectory}"));

                return -1;
            }

            si.WorkingDirectory = session.WorkingDirectory;
        }

        si.CreateNoWindow = true;
        si.RedirectStandardOutput = true;
        si.RedirectStandardError = true;
        si.RedirectStandardInput = true;
        si.StandardOutputEncoding = Encoding.UTF8;
        si.StandardErrorEncoding = Encoding.UTF8;

        // Environment variables
        foreach (var env in session.EnvironmentVariables)
        {
            si.Environment[env.Key] = env.Value;
        }

        var tcsExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => tcsExit.TrySetResult();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                onOutput(new ProcessOutputLine(OutputKind.StandardOutput, e.Data));
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                onOutput(new ProcessOutputLine(OutputKind.StandardError, e.Data));
        };

        try
        {
            if (!process.Start())
            {
                onOutput(new ProcessOutputLine(OutputKind.System, "Failed to start process."));
                return -1;
            }

            onStdinReady?.Invoke(process.StandardInput);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await using var reg = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        onOutput(new ProcessOutputLine(OutputKind.System, "Process terminated by user."));
                        System.Diagnostics.Trace.TraceInformation(
                            $"Process terminated by user: {session.Command} {session.BuildArguments()}");
                    }
                }
                catch (Exception ex)
                {
                    // Kill must never escape: a failed kill would otherwise leave
                    // the exit wait below pending forever (stuck "Terminating...").
                    onOutput(new ProcessOutputLine(
                        OutputKind.System,
                        $"Unable to terminate process: {ex.Message}"));
                    System.Diagnostics.Trace.TraceWarning(
                        $"Process kill failed ({session.Command} {session.BuildArguments()}): {ex}");
                }
            });

            // Cancellation-linked wait: if the process survives the kill above
            // (Exited never fires), the wait still unwinds via ct instead of
            // hanging forever.
            await tcsExit.Task.WaitAsync(ct).ConfigureAwait(false);

            // Ensure async output handlers flush before returning the final exit code.
            // Async form: the process has exited by now (Exited fired), so this
            // completes immediately without blocking a thread-pool thread.
            try
            {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Process already disposed/exited — safe to ignore.
            }

            ct.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onOutput(new ProcessOutputLine(OutputKind.System, $"Error: {ex.Message}"));
            return -1;
        }
    }
}
