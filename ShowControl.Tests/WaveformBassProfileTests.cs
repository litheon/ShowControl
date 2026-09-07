using System;
using System.Linq;
using BeatLink.Data;
using ShowControl.AudioAnalysis;
using ShowControl.Devices.ProDjLink;
using Xunit;

namespace ShowControl.Tests
{
    /// <summary>
    /// Exercises the bass-profile extraction from rekordbox waveform details and the
    /// bass-heavy region detector, plus the engine's published countdown.
    /// </summary>
    public class WaveformBassProfileTests
    {
        [Fact]
        public void FindsSustainedBassRegions()
        {
            // 0..10 s quiet, 10..20 s loud bass, 20..25 s quiet, plus a 0.5 s spike at 22 s
            // that must be rejected (shorter than the minimum region).
            const double hop = 0.1;
            var low = new float[250];
            for (var i = 100; i < 200; i++) low[i] = 1f;
            for (var i = 220; i < 225; i++) low[i] = 1f;

            var starts = WaveformBassProfile.FindBassRegionStarts(low, hop);

            var start = Assert.Single(starts);
            Assert.InRange(start, 9.5, 10.5);
        }

        [Fact]
        public void EmptyOrFlatSeriesYieldsNoRegions()
        {
            Assert.Empty(WaveformBassProfile.FindBassRegionStarts(Array.Empty<float>(), 0.1));
            Assert.Empty(WaveformBassProfile.FindBassRegionStarts(new float[100], 0.1));
        }

        [Fact]
        public void ExtractsLowBandFromThreeBandWaveform()
        {
            // A synthetic 40-second three-band detail (150 half-frames/s, 3 bytes per frame:
            // mid, high, low): mids throughout, bass only in the second half.
            const int frames = 40 * 150;
            var bytes = new byte[frames * 3];
            for (var i = 0; i < frames; i++)
            {
                bytes[i * 3] = 40;                                // mid
                bytes[i * 3 + 1] = 20;                            // high
                bytes[i * 3 + 2] = (byte)(i >= frames / 2 ? 100 : 5);  // low
            }
            var reference = new DataReference(2, BeatLink.CdjStatus.TrackSourceSlot.UsbSlot, 42,
                BeatLink.CdjStatus.TrackType.Rekordbox);
            var detail = new WaveformDetail(reference, bytes, WaveformStyle.ThreeBand);

            var profile = WaveformBassProfile.Extract(detail);

            Assert.NotNull(profile);
            Assert.NotNull(profile.Analysis.LowBand);
            Assert.Equal(WaveformBassProfile.HopSeconds, profile.Analysis.HopSeconds);
            Assert.Equal(profile.Analysis.Rms.Count, profile.Analysis.LowBand!.Count);
            // Low band is normalized: quiet half well below the loud half.
            Assert.True(profile.Analysis.LowBand[10] < 0.2f);
            Assert.True(profile.Analysis.LowBand[^10] > 0.8f);
            // One bass region, starting at the 20-second midpoint.
            var start = Assert.Single(profile.BassStartsTrackSeconds);
            Assert.InRange(start, 19.0, 21.0);
        }

        /// <summary>A 120 BPM link grid covering the given length: beats every 500 ms.</summary>
        private static BeatLink.Data.BeatGrid LinkGrid(int seconds)
        {
            var beats = seconds * 2;
            var reference = new DataReference(2, BeatLink.CdjStatus.TrackSourceSlot.UsbSlot, 42,
                BeatLink.CdjStatus.TrackType.Rekordbox);
            var withinBar = new int[beats];
            var bpm = new int[beats];
            var times = new long[beats];
            for (var i = 0; i < beats; i++)
            {
                withinBar[i] = (i % 4) + 1;
                bpm[i] = 12000;
                times[i] = i * 500L;
            }
            return new BeatLink.Data.BeatGrid(reference, withinBar, bpm, times);
        }

        /// <summary>A textbook drop shape at 0.1 s hops: groove, suction, restoration held.</summary>
        private static float[] DropShapedLowBand(
            int totalHops, int suctionStart, int suctionEnd, float restored = 0.9f)
        {
            var low = new float[totalHops];
            for (var i = 0; i < totalHops; i++)
            {
                low[i] = i >= suctionStart && i < suctionEnd ? 0.02f : restored;
            }
            return low;
        }

        [Fact]
        public void DetectsSuctionThenSustainedRestorationAsDrop()
        {
            // 60 s track: bass groove, suction 29.6–32.0 s, bass slams back on the downbeat at
            // 32.0 s (beat 65 of the 120 BPM grid) and holds — the textbook drop shape.
            var low = DropShapedLowBand(600, 296, 320);
            var drops = WaveformBassProfile.DetectDrops(low, 0.1, LinkGrid(60));

            var drop = Assert.Single(drops);
            Assert.InRange(drop, 31.5, 32.6);
            // And the snap chose a downbeat.
            var beatNumber = (int)Math.Round(drop / 0.5) + 1;
            Assert.Equal(1, LinkGrid(60).GetBeatWithinBar(beatNumber));
        }

        [Fact]
        public void SuctionWithoutRestorationIsNotADrop()
        {
            // The gap resolves into continued quiet — a breakdown, not a drop.
            var low = DropShapedLowBand(600, 300, 600);
            Assert.Empty(WaveformBassProfile.DetectDrops(low, 0.1, LinkGrid(60)));
        }

        [Fact]
        public void RestorationWithoutSuctionIsNotADrop()
        {
            // Constant bass groove: loud throughout, no gap to resolve.
            var low = DropShapedLowBand(600, 0, 0);
            Assert.Empty(WaveformBassProfile.DetectDrops(low, 0.1, LinkGrid(60)));
        }

        [Fact]
        public void OneHitStabAfterSuctionIsRejected()
        {
            // Bass returns for only 0.5 s then vanishes again — no sustained restoration.
            var low = DropShapedLowBand(600, 300, 600);
            for (var i = 330; i < 335; i++) low[i] = 0.9f;
            Assert.Empty(WaveformBassProfile.DetectDrops(low, 0.1, LinkGrid(60)));
        }

        [Fact]
        public void SuctionRunningToTrackEndIsRejected()
        {
            var low = DropShapedLowBand(600, 550, 600);
            Assert.Empty(WaveformBassProfile.DetectDrops(low, 0.1, LinkGrid(60)));
        }

        [Fact]
        public void ShortDipIsNotASuction()
        {
            // A 0.5 s dip is shorter than the minimum suction and must not predict a drop.
            var low = DropShapedLowBand(600, 300, 305);
            Assert.Empty(WaveformBassProfile.DetectDrops(low, 0.1, LinkGrid(60)));
        }

        [Fact]
        public void DetectionReportExplainsCandidateFates()
        {
            // One accepted drop (suction 29.6-32.0 s) and one rejected breakdown: a gap at
            // 45.0-48.0 s whose bass never returns (quiet through 54 s, then groove resumes
            // gently below the restoration threshold is impossible — so end the track quiet).
            var low = DropShapedLowBand(600, 296, 320);
            for (var i = 450; i < 600; i++) low[i] = 0.05f;
            var drops = WaveformBassProfile.DetectDrops(low, 0.1, LinkGrid(60), out var report);

            Assert.Single(drops);
            Assert.Equal(2, report.Candidates.Count);
            Assert.StartsWith("DROP predicted", report.Candidates[0].Outcome);
            Assert.InRange(report.Candidates[0].SuctionStartSeconds, 29.0, 30.5);
            Assert.Contains("ran to track end", report.Candidates[1].Outcome);

            // The statistics expose whether suction windows can form at all.
            Assert.True(report.FractionBelowSuction > 0.2);
            Assert.True(report.FractionAboveRestoration > 0.5);
            Assert.True(report.SmoothedMin < 0.1);

            // And the log rendering carries the whole story in one line.
            var rendered = report.ToString();
            Assert.Contains("DROP predicted", rendered);
            Assert.Contains("ran to track end", rendered);
        }

        [Fact]
        public void DetectionReportShowsWhySuctionNeverForms()
        {
            // A track whose low band never dips below the suction threshold — the report's
            // fifth percentile makes the cause obvious.
            var low = new float[600];
            Array.Fill(low, 0.5f);
            low[0] = 1f;  // normalization reference
            var drops = WaveformBassProfile.DetectDrops(low, 0.1, LinkGrid(60), out var report);

            Assert.Empty(drops);
            Assert.Empty(report.Candidates);
            Assert.True(report.SmoothedFifthPercentile > WaveformBassProfile.SuctionThreshold);
            Assert.Equal(0.0, report.FractionBelowSuction, 3);
        }

        [Fact]
        public void EngineCountsDownToPredictedDropMarkers()
        {
            var metadata = new SyntheticBeatMetadataSource();
            var engine = new AudioAnalysisEngine(beatMetadata: metadata);
            metadata.SetGrid(
                ShowControl.AudioAnalysis.BeatGrid.Regular(firstBeatTime: 0, bpm: 128, beatCount: 2000),
                derivedMarkers: [new SectionMarker(40.0, SectionKind.Drop, 0.7f)]);

            var source = new SyntheticAudioSource(engine);
            source.Generate(SyntheticPreset.Silence, seconds: 1.0);
            engine.ProcessPending();
            var snapshot = engine.Latest;

            Assert.InRange(snapshot.NextPredictedDropIn, 38.5f, 40f);
            // The parallel channels stay independent: no announced drop, no bass region.
            Assert.Equal(-1f, snapshot.NextDropIn);
            Assert.Equal(-1f, snapshot.NextBassIn);
        }

        [Fact]
        public void EngineCountsDownToDerivedBassMarkers()
        {
            var metadata = new SyntheticBeatMetadataSource();
            var engine = new AudioAnalysisEngine(beatMetadata: metadata);
            metadata.SetGrid(
                ShowControl.AudioAnalysis.BeatGrid.Regular(firstBeatTime: 0, bpm: 128, beatCount: 2000),
                derivedMarkers: [new SectionMarker(25.0, SectionKind.BassHeavy, 0.6f)]);

            var source = new SyntheticAudioSource(engine);
            source.Generate(SyntheticPreset.Silence, seconds: 1.0);
            engine.ProcessPending();
            var snapshot = engine.Latest;

            Assert.InRange(snapshot.NextBassIn, 23.5f, 25f);
            // Derived markers stay out of the announced-section predictions.
            Assert.Equal(-1f, snapshot.NextDropIn);
            Assert.Equal(SectionKind.Unknown, snapshot.NextSectionKind);
        }
    }
}
