using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using ZLogger;

namespace CommandPrompts.App.Avalonia.Logging;

/// <summary>
/// Bridges Avalonia framework diagnostics (emitted via
/// <see cref="Trace"/> by <c>LogToTrace</c>) into a ZLogger rolling file
/// under %LOCALAPPDATA%\CommandPrompts\logs, so binding/control/layout
/// errors are captured when the app runs without a debugger.
/// Producer side only enqueues (wait-free); a single worker drains.
/// </summary>
public sealed class AvaloniaTraceListener : TraceListener
{
    private const string Category = "Avalonia";
    private readonly ILogger logger;
    private readonly ConcurrentQueue<Entry> queue = new();
    private readonly CancellationTokenSource cts = new();
    private readonly Task worker;
    private bool disposed;

    public AvaloniaTraceListener()
    {
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CommandPrompts", "logs");
        Directory.CreateDirectory(logsDir);

        var factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddZLoggerRollingFile(
                (timestamp, sequenceNumber) => Path.Combine(
                    logsDir,
                    sequenceNumber == 0
                        ? $"app-{timestamp:yyyy-MM-dd}.log"
                        : $"app-{timestamp:yyyy-MM-dd}.{sequenceNumber}.log"),
                ZLogger.Providers.RollingInterval.Day));

        logger = factory.CreateLogger(Category);

        IndentSize = 0;
        NeedIndent = false;

        worker = Task.Run(() => DrainAsync(cts.Token));
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source,
        TraceEventType eventType, int id, string? message)
        => Enqueue(eventType, message);

    public override void TraceEvent(TraceEventCache? eventCache, string source,
        TraceEventType eventType, int id, string? format, params object?[]? args)
        => Enqueue(eventType, format is null || args is null ? format : string.Format(format, args));

    public override void Write(string? message)
        => Enqueue(TraceEventType.Information, message);

    public override void WriteLine(string? message)
        => Enqueue(TraceEventType.Information, message);

    private void Enqueue(TraceEventType eventType, string? message)
    {
        if (disposed || message is null)
            return;

        queue.Enqueue(new Entry(eventType, message));
    }

    private async Task DrainAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (queue.IsEmpty)
            {
                try
                {
                    await Task.Delay(250, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            while (queue.TryDequeue(out var entry))
            {
                try
                {
                    Emit(entry);
                }
                catch
                {
                    // Never throw into the worker.
                }
            }
        }
    }

    private void Emit(Entry entry)
    {
        switch (entry.EventType)
        {
            case TraceEventType.Critical:
            case TraceEventType.Error:
                logger.ZLogError($"{entry.Message}");
                break;
            case TraceEventType.Warning:
                logger.ZLogWarning($"{entry.Message}");
                break;
            case TraceEventType.Information:
            case TraceEventType.Verbose:
                logger.ZLogInformation($"{entry.Message}");
                break;
            default:
                logger.ZLogDebug($"{entry.Message}");
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            cts.Cancel();

            try
            {
                worker.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Worker cancelled — expected.
            }

            cts.Dispose();
            Trace.Listeners.Remove(this);
        }

        base.Dispose(disposing);
    }

    private readonly record struct Entry(TraceEventType EventType, string Message);
}
