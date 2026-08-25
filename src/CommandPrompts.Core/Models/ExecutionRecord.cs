namespace CommandPrompts.Core.Models;

public sealed class ExecutionRecord
{
    public required DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int? ExitCode { get; set; }
    public List<OutputEntry> Output { get; set; } = [];
}
