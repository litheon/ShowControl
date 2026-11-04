using System;
using ShowControl.PixelMapping;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>
    /// One line in the fixture list.
    /// <para>
    /// A record on purpose: the list is reconciled in place by comparing a freshly built row against
    /// the one already there, and value equality is what makes an unchanged rig touch the platform
    /// list not at all. Clearing and refilling the list instead reaches
    /// <c>UICollectionView.insertItemsAtIndexPaths:</c>, which throws and takes the process down on
    /// Mac Catalyst — so this must not become a mutable observable object.
    /// </para>
    /// </summary>
    public sealed record FixtureRow
    {
        public Guid Id { get; init; }

        public bool Enabled { get; init; }

        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// Name, size, channel range and controller, in that order: the row is one truncating line,
        /// and the channel range is what the operator is reading the list for when wiring a new
        /// strip. Channels are the zero-based offsets the file stores and the add form takes, so
        /// the number here is the number you would type. The full record is in the detail readout.
        /// </summary>
        public static FixtureRow From(FixtureDefinition fixture, IPixelTypeResolver pixelTypes)
        {
            if (fixture == null) throw new ArgumentNullException(nameof(fixture));
            if (pixelTypes == null) throw new ArgumentNullException(nameof(pixelTypes));

            int start = fixture.Output.StartChannel;
            int channels = ChannelPlanner.ChannelCount(fixture, pixelTypes);
            string range = channels > 0 ? $"ch {start}–{start + channels - 1}" : $"ch {start}";
            string endpoint = fixture.Output.Interface == OutputInterfaceType.ArtNet
                ? $"{fixture.Output.IpAddress} u{fixture.Output.Universe}"
                : fixture.Output.IpAddress;

            return new FixtureRow
            {
                Id = fixture.Id,
                Enabled = fixture.Enabled,
                Description = $"{fixture.Name} · {fixture.PixelCount}px · {range} · {endpoint}"
            };
        }
    }
}
