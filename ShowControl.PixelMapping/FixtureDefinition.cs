using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// One addressable run of pixels.
    /// <para>
    /// Geometry is carried as plain positions and nothing else: an origin plus a flat array of
    /// per-pixel offsets. There are deliberately no angle, spacing or pivot fields. Laying out a
    /// line, bending a section about a pivot, mirroring and re-spacing are all editor operations
    /// that rewrite <see cref="PixelOffsets"/>; the file records only the result. That keeps the
    /// render path free of trigonometry and lets editor tooling grow without a schema change.
    /// </para>
    /// </summary>
    public sealed class FixtureDefinition
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Disabled fixtures are excluded from the layout entirely: no pixels, no channels, no
        /// output device. This is the supported replacement for commenting out a device line.
        /// </summary>
        public bool Enabled { get; set; } = true;

        public OutputTarget Output { get; set; } = new OutputTarget();

        /// <summary>Resolved to a channel count (and a DMX fixture) by an <see cref="IPixelTypeResolver"/>.</summary>
        public string PixelType { get; set; } = "WS2812B";

        public ColorOrder ColorOrder { get; set; } = ColorOrder.Rgb;

        /// <summary>Fixture origin in shader-image space. Dragging a fixture moves only this.</summary>
        public float OriginX { get; set; }

        public float OriginY { get; set; }

        /// <summary>Flat <c>[dx0, dy0, dx1, dy1, …]</c> relative to the origin, in shader-image pixels.</summary>
        [JsonConverter(typeof(PixelOffsetsJsonConverter))]
        public float[] PixelOffsets { get; set; } = Array.Empty<float>();

        /// <summary>
        /// Pixels sampled as black. They still occupy their channels, because addressing is
        /// positional on the wire.
        /// </summary>
        public List<PixelRange> BlankedPixels { get; set; } = new List<PixelRange>();

        /// <summary>Derived, never stored, so the count and the offsets cannot disagree.</summary>
        [JsonIgnore]
        public int PixelCount => PixelOffsets.Length / 2;

        public bool IsBlanked(int pixelIndex)
        {
            for (int i = 0; i < BlankedPixels.Count; i++)
            {
                if (BlankedPixels[i].Contains(pixelIndex))
                {
                    return true;
                }
            }
            return false;
        }

        public FixtureDefinition Clone() => new FixtureDefinition
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            Output = Output.Clone(),
            PixelType = PixelType,
            ColorOrder = ColorOrder,
            OriginX = OriginX,
            OriginY = OriginY,
            PixelOffsets = (float[])PixelOffsets.Clone(),
            BlankedPixels = new List<PixelRange>(BlankedPixels)
        };
    }
}
