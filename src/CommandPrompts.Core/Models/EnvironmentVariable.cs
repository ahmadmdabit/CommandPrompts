namespace CommandPrompts.Core.Models;

public sealed class EnvironmentVariable : IEquatable<EnvironmentVariable>
{
    public required string Key { get; set; }
    public required string Value { get; set; }

    public bool Equals(EnvironmentVariable? other)
        => other is not null
        && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as EnvironmentVariable);
    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(Key),
        StringComparer.Ordinal.GetHashCode(Value));
}
