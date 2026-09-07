using System.Linq;
using ShowControl.AudioAnalysis;
using Xunit;

namespace ShowControl.Tests
{
    /// <summary>
    /// The snapshot must make the beat-metadata state visible — presence, whether the PLL is
    /// actually being driven by a grid, and beat packet recency — so the debug view (and any
    /// other consumer) can tell "the wire is feeding us" apart from "the PLL is free-running".
    /// </summary>
    public class MetadataVisibilityTests
    {
        private static (AudioAnalysisEngine Engine, SyntheticBeatMetadataSource Metadata) CreateEngine()
        {
            var metadata = new SyntheticBeatMetadataSource();
            var engine = new AudioAnalysisEngine(beatMetadata: metadata);
            return (engine, metadata);
        }

        private static AnalysisSnapshot RunOneSecond(AudioAnalysisEngine engine)
        {
            var source = new SyntheticAudioSource(engine);
            source.Generate(SyntheticPreset.Silence, seconds: 1.0);
            engine.ProcessPending();
            return engine.Latest;
        }

        [Fact]
        public void AbsentSourceReportsAbsent()
        {
            var (engine, _) = CreateEngine();
            var snapshot = RunOneSecond(engine);
            Assert.Equal(BeatMetadataPresence.Absent, snapshot.MetadataPresence);
            Assert.False(snapshot.MetadataGridActive);
            Assert.Equal(-1f, snapshot.BeatPacketAge);
        }

        [Fact]
        public void ArmedGridReportsFullGridAndGridActive()
        {
            var (engine, metadata) = CreateEngine();
            metadata.SetGrid(BeatGrid.Regular(firstBeatTime: 0, bpm: 128, beatCount: 2000));
            var snapshot = RunOneSecond(engine);

            Assert.Equal(BeatMetadataPresence.FullGrid, snapshot.MetadataPresence);
            Assert.True(snapshot.MetadataGridActive);
            Assert.Equal(128f, snapshot.Bpm, 1f);

            // Losing the grid (the deck pausing, in the live adapter) must show up on the very
            // next published frame, even though the PLL keeps free-running by design.
            metadata.SetBeatsOnly();
            var after = RunOneSecond(engine);
            Assert.Equal(BeatMetadataPresence.BeatsOnly, after.MetadataPresence);
            Assert.False(after.MetadataGridActive);
        }

        [Fact]
        public void AnnouncedSectionsPredictNextBoundaryAndDrop()
        {
            var (engine, metadata) = CreateEngine();
            // Anchor a grid at time zero with a build at 10 s and the drop at 30 s — both well
            // past the one second of audio the test runs, so they stay "ahead".
            metadata.SetGrid(
                BeatGrid.Regular(firstBeatTime: 0, bpm: 128, beatCount: 2000),
                sections:
                [
                    new SectionMarker(0.0, SectionKind.Intro, 0.9f),
                    new SectionMarker(10.0, SectionKind.BuildUp, 0.9f),
                    new SectionMarker(30.0, SectionKind.Drop, 0.9f),
                ]);
            var snapshot = RunOneSecond(engine);

            Assert.Equal(SectionKind.BuildUp, snapshot.NextSectionKind);
            Assert.InRange(snapshot.NextSectionIn, 8.5f, 10f);
            Assert.InRange(snapshot.NextDropIn, 28.5f, 30f);
            // The two countdowns stay in lockstep: drop − section == 20 s of track.
            Assert.Equal(20f, snapshot.NextDropIn - snapshot.NextSectionIn, 0.01f);
        }

        [Fact]
        public void NoUpcomingDropReportsUnknown()
        {
            var (engine, metadata) = CreateEngine();
            metadata.SetGrid(
                BeatGrid.Regular(firstBeatTime: 0, bpm: 128, beatCount: 2000),
                sections: [new SectionMarker(15.0, SectionKind.Outro, 0.9f)]);
            var snapshot = RunOneSecond(engine);

            Assert.Equal(SectionKind.Outro, snapshot.NextSectionKind);
            Assert.True(snapshot.NextSectionIn > 0);
            Assert.Equal(-1f, snapshot.NextDropIn);

            // And with no sections at all, both predictions are unknown.
            metadata.SetGrid(BeatGrid.Regular(firstBeatTime: 0, bpm: 128, beatCount: 2000));
            var bare = RunOneSecond(engine);
            Assert.Equal(SectionKind.Unknown, bare.NextSectionKind);
            Assert.Equal(-1f, bare.NextSectionIn);
            Assert.Equal(-1f, bare.NextDropIn);
        }

        [Fact]
        public void BeatPacketsStampTheirAge()
        {
            var (engine, metadata) = CreateEngine();
            var first = RunOneSecond(engine);
            Assert.Equal(-1f, first.BeatPacketAge);

            metadata.EmitBeat(new BeatObservation(first.Time, 120f, 1, 1f));
            var second = RunOneSecond(engine);
            Assert.InRange(second.BeatPacketAge, 0f, 2f);
        }
    }
}
