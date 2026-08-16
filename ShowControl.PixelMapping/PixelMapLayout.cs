using System.Collections.Generic;

namespace ShowControl.PixelMapping
{
    public enum LayoutSlotKind
    {
        /// <summary>A fixture's pixels and channels.</summary>
        Fixture = 0,

        /// <summary>
        /// Unaddressed channels between two fixtures on the same output. Occupies channels but no
        /// pixels, so the existing sequential surface walk still works when a venue leaves a hole
        /// in a controller's channel space.
        /// </summary>
        Gap
    }

    /// <summary>
    /// One entry in the compiled channel order. Slots are contiguous in both pixel space and
    /// channel space, in this exact order.
    /// </summary>
    public readonly record struct LayoutSlot(
        LayoutSlotKind Kind,
        FixtureDefinition? Fixture,
        int PixelStart,
        int PixelCount,
        int ChannelStart,
        int ChannelCount,
        int ChannelsPerPixel,
        int OutputIndex);

    /// <summary>One physical output device and the global channel range it sends.</summary>
    public readonly record struct OutputSegment(
        OutputTargetKey Target,
        int ChannelStart,
        int ChannelCount);

    /// <summary>
    /// The compiled form of a <see cref="PixelMap"/>, and the single authority for frame ordering,
    /// surface composition order, output device order and channel offsets. Everything downstream
    /// derives from this, which is what removed the old unwritten contract where the device list
    /// and the surface tree had to agree by coincidence of declaration order.
    /// </summary>
    public sealed class PixelMapLayout
    {
        public PixelMapLayout(
            int totalPixelCount,
            int totalChannelCount,
            IReadOnlyList<LayoutSlot> slots,
            IReadOnlyList<OutputSegment> outputs)
        {
            TotalPixelCount = totalPixelCount;
            TotalChannelCount = totalChannelCount;
            Slots = slots;
            Outputs = outputs;
        }

        public int TotalPixelCount { get; }

        public int TotalChannelCount { get; }

        /// <summary>Ordered; drives the render surface tree.</summary>
        public IReadOnlyList<LayoutSlot> Slots { get; }

        /// <summary>Ordered; drives the output device list.</summary>
        public IReadOnlyList<OutputSegment> Outputs { get; }

        public IEnumerable<LayoutSlot> FixtureSlots
        {
            get
            {
                for (int i = 0; i < Slots.Count; i++)
                {
                    if (Slots[i].Kind == LayoutSlotKind.Fixture)
                    {
                        yield return Slots[i];
                    }
                }
            }
        }
    }
}
