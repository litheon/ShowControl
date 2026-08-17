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

        public static FixtureRow From(FixtureDefinition fixture) => new FixtureRow
        {
            Id = fixture.Id,
            Enabled = fixture.Enabled,
            Description = $"{fixture.Name} · {fixture.PixelCount}px · {fixture.Output.Key} ch {fixture.Output.StartChannel + 1}"
        };
    }
}
