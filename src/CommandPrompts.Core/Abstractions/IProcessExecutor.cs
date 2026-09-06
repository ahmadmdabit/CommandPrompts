using CommandPrompts.Core.Models;

namespace CommandPrompts.Core.Abstractions;

public readonly record struct ProcessOutputLine(OutputKind Kind, string Text);

public interface IProcessExecutor
{
    Task<int> ExecuteAsync(
        Session session,
        Action<ProcessOutputLine> onOutput,
        Action<TextWriter>? onStdinReady = null,
        CancellationToken ct = default);
}
