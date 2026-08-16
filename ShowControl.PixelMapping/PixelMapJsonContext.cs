using System.Text.Json.Serialization;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// Source-generated serialization metadata. Required rather than optional: the MacCatalyst
    /// Release publish is trimmed, where reflection-based System.Text.Json degrades silently.
    /// </summary>
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        UseStringEnumConverter = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    [JsonSerializable(typeof(PixelMap))]
    [JsonSerializable(typeof(FixtureDefinition))]
    [JsonSerializable(typeof(OutputTarget))]
    [JsonSerializable(typeof(PixelRange))]
    [JsonSerializable(typeof(PixelMapState))]
    internal sealed partial class PixelMapJsonContext : JsonSerializerContext
    {
    }
}
