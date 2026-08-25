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
public sealed partial class AppJsonContext : JsonSerializerContext;
