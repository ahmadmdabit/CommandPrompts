using CommandPrompts.Core.Models;

namespace CommandPrompts.Core.Abstractions;

public readonly record struct ProcessOutputLine(OutputKind Kind, string Text);

public interface IProcessExecutor
{
    Task ExecuteAsync(
        Session session,
        Action<ProcessOutputLine> onOutput,
        CancellationToken ct = default);
}
