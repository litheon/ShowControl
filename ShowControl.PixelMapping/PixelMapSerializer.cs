using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShowControl.PixelMapping
{
    /// <summary>Raised when a pixel-map file cannot be read. Callers fall through to the next source.</summary>
    public sealed class PixelMapFormatException : Exception
    {
        public PixelMapFormatException(string message) : base(message) { }

        public PixelMapFormatException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class PixelMapSerializer
    {
        public static string Serialize(PixelMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return JsonSerializer.Serialize(map, PixelMapJsonContext.Default.PixelMap);
        }

        public static PixelMap Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new PixelMapFormatException("Pixel map file is empty.");
            }

            PixelMap? map;
            try
            {
                map = JsonSerializer.Deserialize(json, PixelMapJsonContext.Default.PixelMap);
            }
            catch (JsonException ex)
            {
                throw new PixelMapFormatException($"Pixel map file is not valid JSON: {ex.Message}", ex);
            }

            if (map == null)
            {
                throw new PixelMapFormatException("Pixel map file deserialized to nothing.");
            }

            if (map.Version > PixelMap.CurrentVersion)
            {
                throw new PixelMapFormatException(
                    $"Pixel map is version {map.Version}; this build understands up to {PixelMap.CurrentVersion}.");
            }

            // Older files may omit the list entirely rather than writing an empty one.
            map.Fixtures ??= new System.Collections.Generic.List<FixtureDefinition>();
            foreach (var fixture in map.Fixtures)
            {
                fixture.PixelOffsets ??= Array.Empty<float>();
                fixture.BlankedPixels ??= new System.Collections.Generic.List<PixelRange>();
                fixture.Output ??= new OutputTarget();
            }

            return map;
        }

        public static string SerializeState(PixelMapState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return JsonSerializer.Serialize(state, PixelMapJsonContext.Default.PixelMapState);
        }

        public static PixelMapState DeserializeState(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new PixelMapFormatException("Pixel map state file is empty.");
            }

            try
            {
                return JsonSerializer.Deserialize(json, PixelMapJsonContext.Default.PixelMapState)
                    ?? throw new PixelMapFormatException("Pixel map state file deserialized to nothing.");
            }
            catch (JsonException ex)
            {
                throw new PixelMapFormatException($"Pixel map state file is not valid JSON: {ex.Message}", ex);
            }
        }

        /// <summary>Content hash used by the sidecar to detect a half-written or externally edited file.</summary>
        public static string ComputeHash(string content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(hash);
        }
    }
}
