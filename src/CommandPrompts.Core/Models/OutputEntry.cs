namespace CommandPrompts.Core.Models;

public sealed class OutputEntry
{
    public required DateTimeOffset Timestamp { get; set; }
    public required OutputKind Kind { get; set; }
    public required string Text { get; set; }
}

public enum OutputKind : byte
{
    StandardOutput = 0,
    StandardError = 1,
    System = 2
}
