using System;
using System.Collections.Generic;
using System.Linq;
using ShowControl.AudioAnalysis;
using ShowControl.AudioAnalysis.Dsp;
using Xunit;

namespace ShowControl.Tests
{
    public class AudioAnalysisEngineTests
    {
        private static (AudioAnalysisEngine Engine, List<AnalysisSnapshot> Published) CreateEngine(
            AnalysisSettings? settings = null)
        {
            var engine = new AudioAnalysisEngine(settings);
            var published = new List<AnalysisSnapshot>();
            engine.SnapshotPublished += published.Add;
            return (engine, published);
        }

        [Fact]
        public void EnvelopeFollower_IsRateIndependent()
        {
            // The same one-second step input, walked at 1ms and at 10ms, must land in the
            // same place — time constants are in seconds, not in samples.
            var fine = new EnvelopeFollower(attackSeconds: 0.05, releaseSeconds: 0.3);
            var coarse = new EnvelopeFollower(attackSeconds: 0.05, releaseSeconds: 0.3);

            for (int i = 0; i < 1000; i++)
            {
                fine.Process(1.0, 0.001);
            }
            for (int i = 0; i < 100; i++)
            {
                coarse.Process(1.0, 0.010);
            }
            Assert.Equal(fine.Value, coarse.Value, precision: 3);

            for (int i = 0; i < 1000; i++)
            {
                fine.Process(0.0, 0.001);
            }
            for (int i = 0; i < 100; i++)
            {
                coarse.Process(0.0, 0.010);
            }
            Assert.Equal(fine.Value, coarse.Value, precision: 3);
        }

        [Fact]
        public void Stft_PublishesOneFramePerHop()
        {
            var stft = new Stft(windowSize: 2048, hopSize: 512);
            var block = new float[48_000]; // 1 second at 48kHz
            stft.Append(block);

            int frames = 0;
            while (stft.TryComputeFrame())
            {
                frames++;
            }

            // Frame f needs f*hop + window samples: floor((N - window) / hop) + 1.
            Assert.Equal(((48_000 - 2048) / 512) + 1, frames);
        }

        [Fact]
        public void CurrentSpectrum_IsValidInsidePublishHandlerAndLocatesATone()
        {
            // A 1 kHz sine at 48 kHz with a 2048-point window lands its energy at
            // bin round(1000 * 2048 / 48000) = 43. The spectrum span must be readable
            // inside the handler (the documented validity window) with 1024 bins.
            var engine = new AudioAnalysisEngine();
            int observedLength = 0;
            int observedArgmax = -1;
            engine.SnapshotPublished += _ =>
            {
                var spectrum = engine.CurrentSpectrum;
                observedLength = spectrum.Length;
                float best = -1f;
                for (int i = 1; i < spectrum.Length; i++)
                {
                    if (spectrum[i] > best) { best = spectrum[i]; observedArgmax = i; }
                }
            };

            const double freq = 1000.0;
            var block = new float[48_000];
            for (int i = 0; i < block.Length; i++)
            {
                block[i] = (float)Math.Sin(2 * Math.PI * freq * i / 48_000.0);
            }
            engine.Submit(block, 0);
            engine.ProcessPending();

            Assert.Equal(1024, observedLength);
            Assert.InRange(observedArgmax, 41, 45);
        }

        [Fact]
        public void APadPressedAndHeld_InSilence_FloodsTheInjectionTableWithinASecond()
        {
            var engine = new AudioAnalysisEngine();
            var pulseAtHop = new List<float>();
            engine.SnapshotPublished += _ => pulseAtHop.Add(
                engine.CurrentInjectionTable.Slice(InjectionHistory.BandCount * 3, InjectionHistory.BandCount).ToArray().Max());
            // A second of silence first: the baseline is all there is.
            engine.Submit(new float[48_000], 0);
            engine.ProcessPending();
            float baseline = pulseAtHop[^1];
            Assert.InRange(baseline, 0f, 0.3f);

            // Press and hold: every hop from here sees the pad down.
            engine.PadPressed(0);
            engine.SetHeldDiscs(1);
            int before = pulseAtHop.Count;
            engine.Submit(new float[48_000], 1.0);
            engine.ProcessPending();
            var held = pulseAtHop.Skip(before).ToList();
            int hopsPerSecond = held.Count;
            Assert.InRange(hopsPerSecond, 80, 100);
            // Full within 0.7s (the flood's attack), still full at the end of the second.
            Assert.True(held[(int)(hopsPerSecond * 0.7)] > 0.95f, $"at 0.7s: {held[(int)(hopsPerSecond * 0.7)]}");
            Assert.True(held[^1] > 0.95f);
        }

        [Fact]
        public void CurrentSpectrum_IsSilentForSilence()
        {
            var engine = new AudioAnalysisEngine();
            float observedMax = -1f;
            engine.SnapshotPublished += _ =>
            {
                float max = 0f;
                foreach (var v in engine.CurrentSpectrum) max = Math.Max(max, v);
                observedMax = max;
            };
            engine.Submit(new float[48_000], 0);
            engine.ProcessPending();
            Assert.InRange(observedMax, 0f, 1e-6f);
        }

        [Fact]
        public void Engine_PublishesAtHopRate()
        {
            var (engine, published) = CreateEngine();
            var source = new SyntheticAudioSource(engine);

            source.Generate(SyntheticPreset.Silence, seconds: 1.0);
            engine.ProcessPending();

            Assert.Equal(((48_000 - 2048) / 512) + 1, published.Count);
            // Steady-state cadence is one hop.
            var dts = published.Skip(1).Select(s => s.Dt).ToList();
            Assert.All(dts, dt => Assert.Equal(512.0 / 48_000, dt, precision: 6));
        }

        [Fact]
        public void SingleBurst_IsNeverLost_AndImpulseHoldsThreeRenderFrames()
        {
            var (engine, published) = CreateEngine();
            var source = new SyntheticAudioSource(engine);

            source.Generate(SyntheticPreset.SingleBurst, seconds: 1.5);
            engine.ProcessPending();

            // The 3ms burst at t=0.5 must show up in the peak of the interval containing it
            // (max-over-interval, never instantaneous sampling).
            var loud = published.Where(s => s.Peak > 0.1).ToList();
            Assert.NotEmpty(loud);
            Assert.All(loud, s => Assert.InRange(s.Time, 0.45, 0.60));

            // Flux registers the appearance.
            Assert.Contains(published, s => s.FluxTotal > 0 && s.Time is > 0.45 and < 0.60);

            // Impulse triggers and is held at its triggering value for at least ~65ms
            // (3 render frames at 21739µs) before any decay.
            float maxImpulse = published.Max(s => s.Lite.Impulse);
            Assert.True(maxImpulse > 0.5, $"burst out of silence should register strongly, got {maxImpulse}");
            var held = published.Where(s => s.Lite.Impulse >= maxImpulse * 0.999f).ToList();
            double heldSpan = held.Max(s => s.Time) - held.Min(s => s.Time);
            Assert.True(heldSpan >= 0.060, $"impulse held only {heldSpan * 1000:F1}ms");
        }

        [Fact]
        public void HostileInput_ProducesOnlyBoundedNaNFreeOutput()
        {
            var (engine, published) = CreateEngine();

            var hostile = new float[4096];
            for (int i = 0; i < hostile.Length; i++)
            {
                hostile[i] = (i % 5) switch
                {
                    0 => float.NaN,
                    1 => float.PositiveInfinity,
                    2 => float.NegativeInfinity,
                    3 => 1e9f,
                    _ => -1e9f,
                };
            }
            for (int block = 0; block < 20; block++)
            {
                engine.Submit(hostile, block * 4096.0 / 48_000);
            }
            engine.ProcessPending();

            Assert.NotEmpty(published);
            foreach (var s in published)
            {
                AssertFiniteAndInRange(s);
            }
        }

        [Fact]
        public void Silence_ProducesValidFramesWithAdvancingPhase()
        {
            var (engine, published) = CreateEngine();
            var source = new SyntheticAudioSource(engine);

            source.Generate(SyntheticPreset.Silence, seconds: 2.0);
            engine.ProcessPending();

            Assert.NotEmpty(published);
            foreach (var s in published)
            {
                Assert.False(s.SignalPresent);
                Assert.Equal(0f, s.Rms);
                AssertFiniteAndInRange(s);
            }

            // Phase keeps advancing on the fallback tempo: unwrapped beat phase over 2s at
            // 120 BPM crosses 0 more than once.
            int wraps = 0;
            for (int i = 1; i < published.Count; i++)
            {
                if (published[i].BeatPhase < published[i - 1].BeatPhase)
                {
                    wraps++;
                }
            }
            Assert.True(wraps >= 2, $"beat phase wrapped only {wraps} times in 2s of silence");
        }

        [Fact]
        public void Latest_IsValidBeforeFirstPublish()
        {
            using var engine = new AudioAnalysisEngine();
            AssertFiniteAndInRange(engine.Latest);
        }

        private static void AssertFiniteAndInRange(AnalysisSnapshot s)
        {
            Assert.True(double.IsFinite(s.Time));
            Assert.True(double.IsFinite(s.Dt));
            Assert.InRange(s.Rms, 0f, 1f);
            Assert.InRange(s.Peak, 0f, 1f);
            Assert.True(float.IsFinite(s.FluxTotal) && s.FluxTotal >= 0);
            Assert.InRange(s.CentroidHz, 0f, 24_000f);
            Assert.InRange(s.Flatness, 0f, 1f);
            Assert.InRange(s.Bpm, 0f, 400f);
            Assert.InRange(s.TempoConfidence, 0f, 1f);
            Assert.InRange(s.BeatPhase, 0f, 1f);
            Assert.InRange(s.BarPhase, 0f, 1f);
            Assert.InRange(s.Lite.Energy, 0f, 1f);
            Assert.InRange(s.Lite.Impulse, 0f, 1f);
            Assert.InRange(s.Lite.Tension, 0f, 1f);
            Assert.InRange(s.Lite.Phase, 0f, 1f);
            Assert.InRange(s.Lite.Hue, 0f, 1f);
            Assert.InRange(s.Lite.Grain, 0f, 1f);
            Assert.InRange(s.Lite.Motion, 0f, 3f);
            Assert.InRange(s.Lite.Spread, 0f, 1f);
            Assert.All(s.BandEnergy, b => Assert.True(float.IsFinite(b) && b >= 0));
            Assert.All(s.BandFlux, b => Assert.True(float.IsFinite(b) && b >= 0));
        }
    }
}
