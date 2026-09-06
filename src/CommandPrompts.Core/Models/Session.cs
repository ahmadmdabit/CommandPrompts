namespace CommandPrompts.Core.Models;

public sealed class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public List<EnvironmentVariable> EnvironmentVariables { get; set; } = [];
    public string? EnvProfileName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastRunAt { get; set; }
    public List<ExecutionRecord> ExecutionHistory { get; set; } = [];
}
