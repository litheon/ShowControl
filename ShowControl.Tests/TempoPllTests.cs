using System;
using System.Collections.Generic;
using System.Linq;
using ShowControl.AudioAnalysis;
using Xunit;

namespace ShowControl.Tests
{
    public class TempoPllTests
    {
        private const double HopSeconds = 512.0 / 48_000;

        /// <summary>Runs the PLL through time, delivering the given onsets in order and
        /// calling Advance at the analysis hop cadence, as the engine does.</summary>
        private static void Run(TempoPll pll, IEnumerable<double> onsetTimes, double untilTime, float strength = 0.8f)
        {
            var onsets = new Queue<double>(onsetTimes.OrderBy(t => t));
            for (double now = 0; now <= untilTime; now += HopSeconds)
            {
                while (onsets.Count > 0 && onsets.Peek() <= now)
                {
                    pll.OnOnset(new OnsetEvent(onsets.Dequeue(), strength));
                }
                pll.Advance(now);
            }
        }

        private static IEnumerable<double> Clicks(double bpm, double from, double until, Func<int, double>? jitter = null)
        {
            double period = 60.0 / bpm;
            int i = 0;
            for (double t = from; t < until; t += period, i++)
            {
                yield return t + (jitter?.Invoke(i) ?? 0);
            }
        }

        [Fact]
        public void SteadyOnsets_LockTempoAndPhase()
        {
            var pll = new TempoPll();
            Run(pll, Clicks(128, from: 0.5, until: 30), untilTime: 30);

            Assert.InRange(pll.Bpm, 125, 131);
            Assert.True(pll.TempoConfidence > 0.5, $"confidence {pll.TempoConfidence}");

            // Predicted beat phase at the next onset times should sit near 0 (or 1).
            double period = 60.0 / 128;
            double nextClick = 0.5 + Math.Ceiling((30 - 0.5) / period) * period;
            double phase = pll.BeatPhase(nextClick);
            double distance = Math.Min(phase, 1 - phase);
            Assert.True(distance < 0.12, $"phase at click {phase}");
        }

        [Fact]
        public void JitteredOnsets_StillLock()
        {
            // Deterministic ±20ms jitter.
            var pll = new TempoPll();
            Run(pll, Clicks(128, 0.5, 30, i => 0.02 * Math.Sin(i * 2.399)), untilTime: 30);

            Assert.InRange(pll.Bpm, 123, 133);
            Assert.True(pll.TempoConfidence > 0.3, $"confidence {pll.TempoConfidence}");
        }

        [Fact]
        public void DriftingTempo_IsTracked()
        {
            var pll = new TempoPll();
            // 120 → 132 BPM over 40s: generate click times by integrating the ramp.
            var clicks = new List<double>();
            double t = 0.5;
            while (t < 40)
            {
                clicks.Add(t);
                double bpm = 120 + (132 - 120) * (t / 40);
                t += 60.0 / bpm;
            }
            Run(pll, clicks, untilTime: 40);

            Assert.InRange(pll.Bpm, 127, 137);
        }

        [Fact]
        public void DroppedBeats_KeepTheLock()
        {
            var pll = new TempoPll();
            // Every 4th click missing.
            var clicks = Clicks(128, 0.5, 30).Where((_, i) => i % 4 != 3);
            Run(pll, clicks, untilTime: 30);

            Assert.InRange(pll.Bpm, 124, 132);
            Assert.True(pll.TempoConfidence > 0.3);
        }

        [Fact]
        public void Silence_FreeRunsAtLastTempoAndDecaysConfidence()
        {
            var pll = new TempoPll();
            Run(pll, Clicks(128, 0.5, 10), untilTime: 10);
            double lockedBpm = pll.Bpm;
            float lockedConfidence = pll.TempoConfidence;

            // 20s of nothing.
            Run(pll, Enumerable.Empty<double>(), untilTime: 30);

            Assert.Equal(lockedBpm, pll.Bpm, precision: 5);       // tempo held
            Assert.True(pll.TempoConfidence < lockedConfidence);   // honesty decays

            // Phase still advances: two nearby times differ.
            Assert.NotEqual(pll.BeatPhase(30.0), pll.BeatPhase(30.1));
        }

        [Fact]
        public void FullGrid_DrivesPhaseTempoAndDownbeats()
        {
            var source = new SyntheticBeatMetadataSource();
            source.SetGrid(BeatGrid.Regular(firstBeatTime: 1.0, bpm: 174, beatCount: 400));
            var pll = new TempoPll();
            pll.AttachMetadata(source);

            Run(pll, Enumerable.Empty<double>(), untilTime: 10);

            Assert.True(pll.UsingGrid);
            Assert.InRange(pll.Bpm, 173.5, 174.5);
            Assert.Equal(1f, pll.TempoConfidence, precision: 2);

            // Phase 0 lands on grid beats; downbeats have whole-track horizon.
            double phase = pll.BeatPhase(1.0 + 60.0 / 174 * 10);
            Assert.True(Math.Min(phase, 1 - phase) < 0.01, $"phase {phase}");
            double barLength = 4 * 60.0 / 174;
            double expected = 1.0 + Math.Ceiling((10.0 - 1.0) / barLength) * barLength;
            Assert.Equal(expected, pll.NextDownbeat(10.0), precision: 6);
        }

        [Fact]
        public void GridWithOffsetOnsets_TrimsBoundedResidual()
        {
            var source = new SyntheticBeatMetadataSource();
            source.SetGrid(BeatGrid.Regular(firstBeatTime: 1.0, bpm: 128, beatCount: 400));
            var pll = new TempoPll();
            pll.AttachMetadata(source);

            // Audio consistently 30ms after the announced grid (PA latency the metadata
            // cannot know). The trim should absorb it...
            Run(pll, Clicks(128, from: 1.03, until: 30), untilTime: 30);
            double period = 60.0 / 128;
            double clickTime = 1.03 + Math.Ceiling((30 - 1.03) / period) * period;
            double phase = pll.BeatPhase(clickTime);
            double distance = Math.Min(phase, 1 - phase);
            Assert.True(distance < 0.03 * 128 / 60 + 0.02, $"residual phase {phase}");

            // ...but a huge offset is clamped: garbage can't drag phase far off the grid.
            var wild = new TempoPll();
            wild.AttachMetadata(source);
            Run(wild, Clicks(128, from: 1.2, until: 30), untilTime: 30); // 200ms "offset"
            double gridBeat = 1.0 + Math.Ceiling((30 - 1.0) / period) * period;
            double atGridBeat = wild.BeatPhase(gridBeat);
            double gridDistance = Math.Min(atGridBeat, 1 - atGridBeat);
            Assert.True(gridDistance < 0.06 / period + 0.02, $"grid phase pulled to {atGridBeat}");
        }

        [Fact]
        public void GridLossMidTrack_HandsOverWithoutPhaseJump()
        {
            var source = new SyntheticBeatMetadataSource();
            source.SetGrid(BeatGrid.Regular(firstBeatTime: 0.5, bpm: 140, beatCount: 400));
            var pll = new TempoPll();
            pll.AttachMetadata(source);

            Run(pll, Enumerable.Empty<double>(), untilTime: 10);
            double phaseBefore = pll.BeatPhase(10.0);
            double bpmBefore = pll.Bpm;

            source.Clear();
            pll.Advance(10.0 + HopSeconds);

            Assert.False(pll.UsingGrid);
            Assert.Equal(bpmBefore, pll.Bpm, precision: 3);
            double phaseAfter = pll.BeatPhase(10.0 + HopSeconds);
            double expectedAdvance = HopSeconds / (60.0 / 140);
            double delta = phaseAfter - phaseBefore - expectedAdvance;
            delta -= Math.Round(delta);
            Assert.True(Math.Abs(delta) < 0.02, $"phase jumped by {delta}");
        }

        [Fact]
        public void BeatsOnlyPackets_AlignTempoPhaseAndBar()
        {
            var source = new SyntheticBeatMetadataSource();
            source.SetBeatsOnly();
            var pll = new TempoPll();
            pll.AttachMetadata(source);

            double period = 60.0 / 150;
            for (double now = 0; now < 20; now += HopSeconds)
            {
                pll.Advance(now);
                double sinceStart = now - 2.0;
                if (sinceStart >= 0)
                {
                    double positionInBeat = sinceStart % period;
                    if (positionInBeat < HopSeconds)
                    {
                        int beatNumber = (int)Math.Round(sinceStart / period);
                        pll.EmitFor(source, now - positionInBeat, 150f, (beatNumber % 4) + 1);
                    }
                }
            }

            Assert.InRange(pll.Bpm, 149, 151);
            Assert.True(pll.TempoConfidence > 0.5);
            // Downbeats land on beats announced as beat-1: 2.0, 2.0+4*period, ...
            double next = pll.NextDownbeat(20.0);
            double barLength = 4 * period;
            double offset = (next - 2.0) % barLength;
            double barDistance = Math.Min(offset, barLength - offset);
            Assert.True(barDistance < 0.05, $"downbeat off by {barDistance}");
        }
    }

    internal static class PllTestExtensions
    {
        public static void EmitFor(this TempoPll pll, SyntheticBeatMetadataSource source,
            double time, float bpm, int beatWithinBar)
        {
            _ = pll;
            source.EmitBeat(new BeatObservation(time, bpm, beatWithinBar, 1f));
        }
    }
}
