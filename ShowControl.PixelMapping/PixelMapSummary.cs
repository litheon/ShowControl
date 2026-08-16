using System;
using System.Collections.Generic;

namespace ShowControl.PixelMapping
{
    /// <summary>One fixture as an operator sees it. Channel numbers are 1-based, as printed on rigs.</summary>
    public readonly record struct PixelMapSummaryLine(
        string Name,
        string PixelType,
        int PixelCount,
        string Target,
        int FirstChannel,
        int LastChannel,
        bool Enabled)
    {
        public override string ToString() => Enabled
            ? $"{Name} · {PixelCount}px {PixelType} · {Target} ch {FirstChannel}-{LastChannel}"
            : $"{Name} · {PixelCount}px {PixelType} · {Target} · disabled";
    }

    /// <summary>
    /// The rig as a short list, for the management UI and the startup log.
    /// <para>
    /// Replaces the old address dump, which listed one line per <c>IDmxDevice</c> — around 11,500
    /// lines and 450 KB, since every pixel was its own device.
    /// </para>
    /// </summary>
    public sealed class PixelMapSummary
    {
        private PixelMapSummary(IReadOnlyList<PixelMapSummaryLine> lines, int totalPixelCount, int totalChannelCount, int outputCount)
        {
            Lines = lines;
            TotalPixelCount = totalPixelCount;
            TotalChannelCount = totalChannelCount;
            OutputCount = outputCount;
        }

        public IReadOnlyList<PixelMapSummaryLine> Lines { get; }

        public int TotalPixelCount { get; }

        public int TotalChannelCount { get; }

        public int OutputCount { get; }

        public static PixelMapSummary FromLayout(PixelMap map, PixelMapLayout layout, IPixelTypeResolver pixelTypes)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (pixelTypes == null) throw new ArgumentNullException(nameof(pixelTypes));

            var lines = new List<PixelMapSummaryLine>(map.Fixtures.Count);
            foreach (var fixture in map.Fixtures)
            {
                pixelTypes.TryGetChannelCount(fixture.PixelType, out int channelsPerPixel);
                int first = fixture.Output.StartChannel + 1;
                int last = fixture.Output.StartChannel + (fixture.PixelCount * channelsPerPixel);

                lines.Add(new PixelMapSummaryLine(
                    string.IsNullOrWhiteSpace(fixture.Name) ? fixture.Id.ToString() : fixture.Name,
                    fixture.PixelType,
                    fixture.PixelCount,
                    fixture.Output.Key.ToString(),
                    first,
                    last,
                    fixture.Enabled));
            }

            return new PixelMapSummary(lines, layout.TotalPixelCount, layout.TotalChannelCount, layout.Outputs.Count);
        }
    }
}
