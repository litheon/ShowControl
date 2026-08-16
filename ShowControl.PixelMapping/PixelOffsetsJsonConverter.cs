using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// Writes a fixture's offsets as one space-separated <c>"x,y x,y …"</c> string rather than a
    /// JSON array.
    /// <para>
    /// <c>WriteIndented</c> is on so the rest of the file stays hand-editable, and an indented array
    /// would put each of a 720-pixel strip's 1440 numbers on its own line. Since dragging a fixture
    /// moves only its origin, this string is stable across ordinary edits and a positional change
    /// shows up as a single changed line.
    /// </para>
    /// </summary>
    public sealed class PixelOffsetsJsonConverter : JsonConverter<float[]>
    {
        public override float[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return Array.Empty<float>();
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected pixel offsets as a string, found {reader.TokenType}.");
            }

            string? text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<float>();
            }

            var values = new List<float>();
            foreach (var pair in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int comma = pair.IndexOf(',');
                if (comma < 0)
                {
                    throw new JsonException($"Pixel offset '{pair}' is not an 'x,y' pair.");
                }

                if (!float.TryParse(pair.AsSpan(0, comma), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(pair.AsSpan(comma + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                {
                    throw new JsonException($"Pixel offset '{pair}' is not a pair of numbers.");
                }

                values.Add(x);
                values.Add(y);
            }

            return values.ToArray();
        }

        public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options)
        {
            if (value == null || value.Length == 0)
            {
                writer.WriteStringValue(string.Empty);
                return;
            }

            var builder = new StringBuilder(value.Length * 4);
            for (int i = 0; i + 1 < value.Length; i += 2)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }
                // Default float formatting on .NET Core is shortest-round-trippable.
                builder.Append(value[i].ToString(CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(value[i + 1].ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteStringValue(builder.ToString());
        }
    }
}
