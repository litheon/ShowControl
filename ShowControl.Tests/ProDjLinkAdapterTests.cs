using System;
using System.Collections.Generic;
using BeatLink.CrateDigger.Pdb;
using BeatLink.Data;
using ShowControl.AudioAnalysis;
using ShowControl.Devices.ProDjLink;
using Xunit;
using LinkBeatGrid = BeatLink.Data.BeatGrid;

namespace ShowControl.Tests;

/// <summary>
/// Exercises the pure translation logic of the Pro DJ Link beat metadata adapter: projecting a
/// rekordbox beat grid (track timeline, milliseconds) onto the local capture clock, mapping
/// rekordbox phrase moods to section kinds, and the preferred/fallback source selection. The
/// network plumbing itself is covered by BeatLinkNet's own suite.
/// </summary>
public class ProDjLinkAdapterTests
{
    /// <summary>An eight-beat 120 BPM rekordbox grid starting at track time zero.</summary>
    private static LinkBeatGrid EightBeatLinkGrid()
    {
        var reference = new DataReference(2, BeatLink.CdjStatus.TrackSourceSlot.UsbSlot, 42,
            BeatLink.CdjStatus.TrackType.Rekordbox);
        return new LinkBeatGrid(reference,
            beatWithinBarValues: [1, 2, 3, 4, 1, 2, 3, 4],
            bpmValues: [12000, 12000, 12000, 12000, 12000, 12000, 12000, 12000],
            timeWithinTrackValues: [0, 500, 1000, 1500, 2000, 2500, 3000, 3500]);
    }

    [Fact]
    public void ProjectGridAnchorsTrackTimelineOntoLocalClock()
    {
        // The deck reports track position 1.0 s at local time 100.0 s, playing at normal speed:
        // beat 3 (track 1.0 s) lands exactly at local 100.0, beat 1 half a second earlier.
        var grid = ProDjLinkBeatMetadataSource.ProjectGrid(
            EightBeatLinkGrid(), anchorLocalSeconds: 100.0, trackSecondsAtAnchor: 1.0, playbackRate: 1.0);

        Assert.Equal(8, grid.BeatCount);
        Assert.Equal(99.0, grid.BeatTimeAt(0), 9);
        Assert.Equal(100.0, grid.BeatTimeAt(2), 9);
        Assert.Equal(102.5, grid.BeatTimeAt(7), 9);
        Assert.Equal(1, grid.BeatWithinBarAt(0));
        Assert.Equal(1, grid.BeatWithinBarAt(4));
        Assert.Equal(120.0, grid.BpmAt(100.0), 6);
    }

    [Fact]
    public void ProjectGridScalesByPlaybackRate()
    {
        // At +8% pitch the beats arrive faster on the local clock: the 500 ms track interval
        // spans 500/1.08 ms locally, so effective tempo reads 129.6 BPM.
        var grid = ProDjLinkBeatMetadataSource.ProjectGrid(
            EightBeatLinkGrid(), anchorLocalSeconds: 50.0, trackSecondsAtAnchor: 0.0, playbackRate: 1.08);

        Assert.Equal(50.0, grid.BeatTimeAt(0), 9);
        Assert.Equal(50.0 + 0.5 / 1.08, grid.BeatTimeAt(1), 9);
        Assert.Equal(120.0 * 1.08, grid.BpmAt(50.1), 6);
    }

    [Fact]
    public void ProjectGridHandlesAnchorMidTrack()
    {
        // Anchoring at track 2.0 s places earlier beats in the local past.
        var grid = ProDjLinkBeatMetadataSource.ProjectGrid(
            EightBeatLinkGrid(), anchorLocalSeconds: 10.0, trackSecondsAtAnchor: 2.0, playbackRate: 1.0);

        Assert.Equal(8.0, grid.BeatTimeAt(0), 9);   // Track 0 s → 2 s before the anchor.
        Assert.Equal(10.0, grid.BeatTimeAt(4), 9);  // Track 2 s → the anchor itself.
    }

    [Theory]
    [InlineData(RekordboxAnlz.TrackMood.High, (int)RekordboxAnlz.MoodHighPhrase.Intro, SectionKind.Intro)]
    [InlineData(RekordboxAnlz.TrackMood.High, (int)RekordboxAnlz.MoodHighPhrase.Up, SectionKind.BuildUp)]
    [InlineData(RekordboxAnlz.TrackMood.High, (int)RekordboxAnlz.MoodHighPhrase.Down, SectionKind.Breakdown)]
    [InlineData(RekordboxAnlz.TrackMood.High, (int)RekordboxAnlz.MoodHighPhrase.Chorus, SectionKind.Drop)]
    [InlineData(RekordboxAnlz.TrackMood.High, (int)RekordboxAnlz.MoodHighPhrase.Outro, SectionKind.Outro)]
    [InlineData(RekordboxAnlz.TrackMood.Mid, (int)RekordboxAnlz.MoodMidPhrase.Verse3, SectionKind.Verse)]
    [InlineData(RekordboxAnlz.TrackMood.Mid, (int)RekordboxAnlz.MoodMidPhrase.Bridge, SectionKind.Breakdown)]
    [InlineData(RekordboxAnlz.TrackMood.Mid, (int)RekordboxAnlz.MoodMidPhrase.Chorus, SectionKind.Chorus)]
    [InlineData(RekordboxAnlz.TrackMood.Low, (int)RekordboxAnlz.MoodLowPhrase.Verse1b, SectionKind.Verse)]
    [InlineData(RekordboxAnlz.TrackMood.Low, (int)RekordboxAnlz.MoodLowPhrase.Outro, SectionKind.Outro)]
    public void MapKindCoreTranslatesPhrases(RekordboxAnlz.TrackMood mood, int phraseId, SectionKind expected)
    {
        Assert.Equal(expected, ProDjLinkBeatMetadataSource.MapKindCore(mood, phraseId));
    }

    private sealed class StubSource : IBeatMetadataSource
    {
        public BeatMetadataPresence Presence { get; set; } = BeatMetadataPresence.Absent;
        public AudioAnalysis.BeatGrid? CurrentGrid { get; set; }
        public IReadOnlyList<SectionMarker> Sections { get; set; } = Array.Empty<SectionMarker>();
        public IReadOnlyList<SectionMarker> DerivedMarkers { get; set; } = Array.Empty<SectionMarker>();
        public TrackAnalysis? TrackAnalysis { get; set; }
        public event Action<BeatObservation>? BeatObserved;
        public void Emit(BeatObservation beat) => BeatObserved?.Invoke(beat);
    }

    [Fact]
    public void PreferringSourceSelectsPreferredWhenPresent()
    {
        var preferred = new StubSource();
        var fallback = new StubSource
        {
            Presence = BeatMetadataPresence.FullGrid,
            CurrentGrid = AudioAnalysis.BeatGrid.Regular(0, 120, 8),
        };
        var composite = new PreferringBeatMetadataSource(preferred, fallback);

        // Preferred is dark: the fallback (synthetic/debug) shows through.
        Assert.Equal(BeatMetadataPresence.FullGrid, composite.Presence);
        Assert.Same(fallback.CurrentGrid, composite.CurrentGrid);

        // Real gear appears: the preferred source takes over completely.
        preferred.Presence = BeatMetadataPresence.BeatsOnly;
        Assert.Equal(BeatMetadataPresence.BeatsOnly, composite.Presence);
        Assert.Null(composite.CurrentGrid);
    }

    [Fact]
    public void PreferringSourceForwardsDerivedMarkersFromActiveSource()
    {
        // Regression: DerivedMarkers was once a default interface member, and the composite
        // silently served the empty default instead of the adapter's predictions — pdrop and
        // bass froze on screen. Every surface of the contract must forward.
        var preferred = new StubSource
        {
            DerivedMarkers = new[] { new SectionMarker(10.0, SectionKind.Drop, 0.7f) },
        };
        var fallback = new StubSource
        {
            Presence = BeatMetadataPresence.FullGrid,
            DerivedMarkers = new[] { new SectionMarker(20.0, SectionKind.BassHeavy, 0.6f) },
        };
        var composite = new PreferringBeatMetadataSource(preferred, fallback);

        Assert.Same(fallback.DerivedMarkers, composite.DerivedMarkers);   // Preferred dark.
        preferred.Presence = BeatMetadataPresence.FullGrid;               // Gear appears.
        Assert.Same(preferred.DerivedMarkers, composite.DerivedMarkers);
    }

    [Fact]
    public void PreferringSourceForwardsBeatsOnlyFromActiveSource()
    {
        var preferred = new StubSource();
        var fallback = new StubSource { Presence = BeatMetadataPresence.BeatsOnly };
        var composite = new PreferringBeatMetadataSource(preferred, fallback);
        var received = new List<BeatObservation>();
        composite.BeatObserved += received.Add;

        fallback.Emit(new BeatObservation(1.0, 120f, 1, 1f));       // Active → forwarded.
        preferred.Emit(new BeatObservation(2.0, 128f, 2, 1f));      // Inactive → dropped.
        Assert.Single(received);
        Assert.Equal(1.0, received[0].LocalTime);

        preferred.Presence = BeatMetadataPresence.BeatsOnly;        // Gear appears.
        preferred.Emit(new BeatObservation(3.0, 128f, 3, 1f));      // Now forwarded.
        fallback.Emit(new BeatObservation(4.0, 120f, 4, 1f));       // Now dropped.
        Assert.Equal(2, received.Count);
        Assert.Equal(3.0, received[1].LocalTime);
    }
}
