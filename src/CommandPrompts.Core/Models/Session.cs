namespace CommandPrompts.Core.Models;

public sealed class Session
{
    private static readonly char[] ArgumentLineSeparators = ['\r', '\n'];

    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string Command { get; set; } = string.Empty;
    /// <summary>
    /// Arguments in display form: one argument per line, as edited in the
    /// multi-line box. Use <see cref="BuildArguments"/> for the command line
    /// actually handed to the process.
    /// </summary>
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public List<EnvironmentVariable> EnvironmentVariables { get; set; } = [];
    public string? EnvProfileName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastRunAt { get; set; }
    public List<ExecutionRecord> ExecutionHistory { get; set; } = [];

    /// <summary>
    /// Parses <see cref="Arguments"/> into the individual arguments handed to the process.
    /// Each non-blank line is one or more arguments: a line wrapped in double quotes becomes
    /// a single argument (the surrounding quotes are removed); every other line is split on
    /// whitespace. The result is safe to feed to <see cref="ProcessStartInfo.ArgumentList"/>.
    /// </summary>
    public IEnumerable<string> GetProcessArguments()
    {
        if (string.IsNullOrEmpty(Arguments))
            yield break;

        foreach (var raw in Arguments.Split(ArgumentLineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            // A quoted line ("...") is a single argument; strip the surrounding quotes.
            if (line.Length >= 2 && line[0] == '"' && line[^1] == '"')
            {
                yield return line[1..^1];
                continue;
            }

            // Any other line is split on whitespace into separate arguments.
            foreach (var part in line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                yield return part;
        }
    }

    /// <summary>
    /// Renders the parsed arguments into a display command line: arguments containing
    /// whitespace are wrapped in double quotes.
    /// </summary>
    public string BuildArguments()
        => string.Join(' ', GetProcessArguments()
            .Select(static a => a.Contains(' ', StringComparison.Ordinal)
                    || a.Contains('\t', StringComparison.Ordinal)
                ? QuoteArgument(a)
                : a));

    // Pragmatic quoting for display only. Execution uses ProcessStartInfo.ArgumentList,
    // which performs correct per-platform escaping (H1).
    private static string QuoteArgument(string argument)
        => $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
