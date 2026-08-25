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

    public DebouncedAction(Func<CancellationToken, Task> action, TimeSpan delay)
    {
        this.action = action;
        this.delay = delay;
    }

    public void Invoke()
    {
        lock (locker)
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();
            var token = cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                    await action(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Debounce reset — expected
                }
            }, token);
        }
    }

    public void Dispose()
    {
        lock (locker)
        {
            cts?.Cancel();
            cts?.Dispose();
        }
    }
}
