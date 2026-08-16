using System;
using System.Collections.Generic;

namespace ShowControl.PixelMapping
{
    public static class PixelMapLayoutBuilder
    {
        /// <summary>
        /// Compiles a map into its channel and pixel layout.
        /// <para>
        /// Ordering rules, which the default map depends on: output devices appear in order of the
        /// first enabled fixture that targets them, and fixtures within one output are ordered by
        /// their explicit <see cref="OutputTarget.StartChannel"/>. Disabled fixtures are dropped
        /// entirely.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The map is not renderable — validate with <see cref="PixelMapValidator"/> first. This
        /// throws rather than repairing so a bad map can never reach the render thread silently.
        /// </exception>
        public static PixelMapLayout Build(PixelMap map, IPixelTypeResolver pixelTypes)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (pixelTypes == null) throw new ArgumentNullException(nameof(pixelTypes));

            var outputOrder = new List<OutputTargetKey>();
            var byOutput = new Dictionary<OutputTargetKey, List<FixtureDefinition>>();

            foreach (var fixture in map.Fixtures)
            {
                if (!fixture.Enabled || fixture.PixelCount == 0)
                {
                    continue;
                }

                var key = fixture.Output.Key;
                if (!byOutput.TryGetValue(key, out var members))
                {
                    members = new List<FixtureDefinition>();
                    byOutput.Add(key, members);
                    outputOrder.Add(key);
                }
                members.Add(fixture);
            }

            var slots = new List<LayoutSlot>();
            var outputs = new List<OutputSegment>(outputOrder.Count);
            int pixelCursor = 0;
            int channelCursor = 0;

            for (int outputIndex = 0; outputIndex < outputOrder.Count; outputIndex++)
            {
                var key = outputOrder[outputIndex];
                var members = byOutput[key];
                members.Sort(static (a, b) => a.Output.StartChannel.CompareTo(b.Output.StartChannel));

                int outputChannelStart = channelCursor;
                int cursorWithinOutput = 0;

                foreach (var fixture in members)
                {
                    if (!pixelTypes.TryGetChannelCount(fixture.PixelType, out int channelsPerPixel))
                    {
                        throw new ArgumentException(
                            $"Fixture '{fixture.Name}' uses unknown pixel type '{fixture.PixelType}'.",
                            nameof(map));
                    }

                    int start = fixture.Output.StartChannel;
                    if (start < cursorWithinOutput)
                    {
                        throw new ArgumentException(
                            $"Fixture '{fixture.Name}' starts at channel {start} on {key}, which overlaps the fixture before it.",
                            nameof(map));
                    }

                    if (start > cursorWithinOutput)
                    {
                        int gap = start - cursorWithinOutput;
                        slots.Add(new LayoutSlot(
                            LayoutSlotKind.Gap,
                            Fixture: null,
                            PixelStart: pixelCursor,
                            PixelCount: 0,
                            ChannelStart: channelCursor,
                            ChannelCount: gap,
                            ChannelsPerPixel: 0,
                            OutputIndex: outputIndex));
                        channelCursor += gap;
                        cursorWithinOutput += gap;
                    }

                    int channelCount = fixture.PixelCount * channelsPerPixel;
                    slots.Add(new LayoutSlot(
                        LayoutSlotKind.Fixture,
                        fixture,
                        PixelStart: pixelCursor,
                        PixelCount: fixture.PixelCount,
                        ChannelStart: channelCursor,
                        ChannelCount: channelCount,
                        ChannelsPerPixel: channelsPerPixel,
                        OutputIndex: outputIndex));

                    pixelCursor += fixture.PixelCount;
                    channelCursor += channelCount;
                    cursorWithinOutput += channelCount;
                }

                outputs.Add(new OutputSegment(key, outputChannelStart, channelCursor - outputChannelStart));
            }

            return new PixelMapLayout(pixelCursor, channelCursor, slots, outputs);
        }
    }
}
