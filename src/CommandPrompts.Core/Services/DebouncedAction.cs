using System.Diagnostics;

namespace CommandPrompts.Core.Services;

/// <summary>
/// Coalesces rapid invocations into a single delayed execution.
/// Used for debouncing session file writes.
/// </summary>
public sealed class DebouncedAction : IDisposable
{
    private readonly Func<CancellationToken, Task> action;
    private readonly TimeSpan delay;
    private CancellationTokenSource? cts;
    private readonly Lock locker = new();
    private bool disposed;

    public DebouncedAction(Func<CancellationToken, Task> action, TimeSpan delay)
    {
        this.action = action;
        this.delay = delay;
    }

    public void Invoke()
    {
        CancellationTokenSource? previous;
        var current = new CancellationTokenSource();

        lock (locker)
        {
            if (disposed)
            {
                current.Dispose();
                return;
            }

            previous = cts;
            cts = current;
        }

        previous?.Cancel();

        _ = RunAsync(current);
    }

    /// <summary>Run the pending action immediately without further debouncing (e.g. from Dispose paths).</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        CancellationTokenSource? toAwait;
        lock (locker)
        {
            toAwait = cts;
            cts = null;
        }

        if (toAwait is null)
            return;

        ct.ThrowIfCancellationRequested();
        try
        {
            await action(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        finally
        {
            toAwait.Dispose();
        }
    }

    private async Task RunAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(delay, source.Token).ConfigureAwait(false);
            await action(source.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            // Debounce reset/dispose — expected.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Debounced action failed: {ex}");
        }
        finally
        {
            lock (locker)
            {
                if (ReferenceEquals(cts, source))
                    cts = null;
            }

            source.Dispose();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? current;
        lock (locker)
        {
            disposed = true;
            current = cts;
            cts = null;
        }

        current?.Cancel();
    }
}
