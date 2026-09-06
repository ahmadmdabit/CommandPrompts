using System.Text.Json.Serialization;

using CommandPrompts.Core.Models;

namespace CommandPrompts.Core.Json;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Session))]
[JsonSerializable(typeof(List<Session>))]
[JsonSerializable(typeof(OutputEntry))]
[JsonSerializable(typeof(EnvProfile))]
[JsonSerializable(typeof(List<EnvProfile>))]
public sealed partial class AppJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(OutputEntry))]
public sealed partial class JournalJsonContext : JsonSerializerContext;
