namespace CommandPrompts.Core.Models;

public sealed class EnvProfile
{
    public required string Name { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new();
}
