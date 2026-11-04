using System;
using System.Collections.Generic;
using System.Linq;
using ShowControl.AudioAnalysis;
using Xunit;

namespace ShowControl.Tests
{
    public class InjectionPulsesTests
    {
        private const double Hop = 512.0 / 48_000;
        private static readonly AnalysisSettings Settings = new();
        private static readonly float Baseline = (float)Settings.PulseBaseline;
        private const float Loud = 1f;   // a level well over the floor

        private static InjectionPulses NewPulses() => new(Settings);

        private static float[] Ratios(float value)
        {
            var r = new float[InjectionPulses.BandCount];
            Array.Fill(r, value);
            return r;
        }

        private readonly record struct Sample(float Pulse, float Turbulence);

        /// <summary>Runs <paramref name="seconds"/> of a constant level and returns band 0's
        /// dye pulse and the turbulence per hop.</summary>
        private static List<Sample> Run(InjectionPulses pulses, double seconds, float ratio, float level = Loud)
        {
            var trace = new List<Sample>();
            int hops = (int)Math.Round(seconds / Hop);
            for (int i = 0; i < hops; i++)
            {
                pulses.Advance(Ratios(ratio), Ratios(level), Hop);
                trace.Add(new Sample(Peak(pulses), pulses.Turbulence));
            }
            return trace;
        }

        /// <summary>The strongest band's pulse: the pad's flood is on one disc at a time.</summary>
        private static float Peak(InjectionPulses pulses)
        {
            float max = 0f;
            foreach (var p in pulses.Pulses) max = Math.Max(max, p);
            return max;
        }

        // The baseline ranges up to twice its nominal value with the band's level;
        // an accent goes to 1. Anything under half is the baseline alone.
        private static bool NoAccent(float pulse) => pulse < 0.5f;
        private static bool Quiet(Sample s) => NoAccent(s.Pulse) && s.Turbulence < 1e-4f;

        /// <summary>Fires one hit at <paramref name="level"/> and returns the peak dye pulse
        /// and peak turbulence it causes, leaving the band idle again afterwards.</summary>
        private static Sample PeakOfAHit(InjectionPulses pulses, float level)
        {
            pulses.Advance(Ratios(2f), Ratios(level), Hop);
            var trace = Run(pulses, InjectionPulses.PulseSeconds + 0.1, ratio: 0f, level: 0f);
            return new Sample(trace.Max(s => s.Pulse), trace.Max(s => s.Turbulence));
        }

        [Fact]
        public void BelowUsual_NothingFires_ButTheBaselineIsThere()
        {
            var pulses = NewPulses();
            Assert.True(Run(pulses, 3, ratio: 0.9f).All(Quiet));
        }

        [Fact]
        public void QuietMaterial_FiresOnTheRatioAlone_ThereIsNoLevelFloor()
        {
            var pulses = NewPulses();
            Run(pulses, 0.5, ratio: 4f, level: 0.001f);
            Assert.True(pulses.Turbulence > 0.1f);
            // Only outright silence cannot fire, whatever ratio it is handed.
            Assert.True(Run(NewPulses(), 3, ratio: 4f, level: 0f).All(Quiet));
        }

        [Fact]
        public void JustAboveUsual_DoesNotFire()
        {
            var pulses = NewPulses();
            Assert.True(Run(pulses, 3, ratio: (float)Settings.PulseTriggerRatio - 0.05f).All(Quiet));
        }

        [Fact]
        public void AHit_RunsItsWholeCourse_AfterTheLevelHasGone()
        {
            var pulses = NewPulses();
            pulses.Advance(Ratios(2f), Ratios(Loud), Hop);     // one hop above usual
            var after = Run(pulses, InjectionPulses.PulseSeconds * 0.9, ratio: 0f, level: 0f);

            Assert.True(after[0].Turbulence > 0f);
            Assert.Contains(after, s => s.Turbulence > 0.99f);  // reached its hold at full
            Assert.True(after[^1].Turbulence > 0.01f);          // still releasing near the end
            var later = Run(pulses, InjectionPulses.PulseSeconds * 0.2, ratio: 0f, level: 0f);
            Assert.True(Quiet(later[^1]));                      // and done
        }

        [Fact]
        public void ASustainedLevel_FiresOnePulseAtATime_EachCompleteBeforeTheNext()
        {
            var pulses = NewPulses();
            var trace = Run(pulses, 12, ratio: 1.5f);

            // Count starts: a rise from nothing.
            var starts = new List<int>();
            for (int i = 1; i < trace.Count; i++)
            {
                if (trace[i - 1].Turbulence < 1e-4f && trace[i].Turbulence > 1e-4f) starts.Add(i);
            }
            int expected = (int)(12 / InjectionPulses.PulseSeconds);
            Assert.InRange(starts.Count, expected - 1, expected + 1);
            for (int i = 1; i < starts.Count; i++)
            {
                double gap = (starts[i] - starts[i - 1]) * Hop;
                Assert.InRange(gap, InjectionPulses.PulseSeconds - 2 * Hop, InjectionPulses.PulseSeconds + 2 * Hop);
            }
        }

        [Fact]
        public void ALouderHitDuringAPulse_NeitherRestartsNorExtendsIt()
        {
            var pulses = NewPulses();
            pulses.Advance(Ratios(2f), Ratios(Loud), Hop);
            Run(pulses, InjectionPulses.AttackSeconds + InjectionPulses.HoldSeconds * 0.5, ratio: 0f, level: 0f);
            var before = new Sample(pulses.Pulses[0], pulses.Turbulence);
            pulses.Advance(Ratios(5f), Ratios(Loud * 2f), Hop);  // a much louder hit mid-pulse
            Assert.Equal(before.Pulse, pulses.Pulses[0], 3);     // ignored
            Assert.Equal(before.Turbulence, pulses.Turbulence, 3);

            var rest = Run(pulses, InjectionPulses.PulseSeconds, ratio: 0f, level: 0f);
            Assert.True(Quiet(rest[^1]));                        // ends on the original schedule
        }

        [Fact]
        public void OrdinaryHits_StirTheFlow_AndInjectNoDye()
        {
            var pulses = NewPulses();
            var first = PeakOfAHit(pulses, Loud);                // the first hit: reference and accent
            Assert.Equal(1f, first.Pulse, 3);
            Assert.Equal(1f, first.Turbulence, 3);               // an accent stirs as well
            Assert.False(pulses.AccentReady);

            // The accent is spent; hits of any level now only stir, all alike.
            foreach (float level in new[] { Loud * 0.5f, Loud * 0.1f, Loud })
            {
                var peak = PeakOfAHit(pulses, level);
                Assert.True(NoAccent(peak.Pulse));               // no dye beyond the baseline
                Assert.Equal(1f, peak.Turbulence, 3);
            }
        }

        [Fact]
        public void TheAccent_WaitsForAHitAtTheLoudest()
        {
            var pulses = NewPulses();
            PeakOfAHit(pulses, Loud);                              // spends the accent, sets the reference
            Run(pulses, Settings.PulseAccentSeconds, ratio: 0f, level: Loud * 0.05f);  // rested; keeps the reference up
            Assert.True(pulses.AccentReady);

            // Under the loudest: a stir, and the accent is still waiting.
            Assert.True(NoAccent(PeakOfAHit(pulses, Loud * (float)Settings.PulseAccentRatio * 0.9f).Pulse));
            Assert.True(pulses.AccentReady);
            // At it: dye at full, from every band.
            Assert.Equal(1f, PeakOfAHit(pulses, Loud).Pulse, 3);
            Assert.False(pulses.AccentReady);
            Assert.All(pulses.Pulses.ToArray(), p => Assert.Equal(pulses.Pulses[0], p));
        }

        [Fact]
        public void ALouderHitThanAnyBefore_IsTheNewPeak_AndAccents()
        {
            var pulses = NewPulses();
            PeakOfAHit(pulses, Loud);
            Run(pulses, Settings.PulseAccentSeconds, ratio: 0f, level: Loud * 0.05f);
            Assert.Equal(1f, PeakOfAHit(pulses, Loud * 2f).Pulse, 3);
            Assert.Equal(Loud * 2f, pulses.ReferenceOf(0), 3);
        }

        [Fact]
        public void DyePulses_AreRare_OnASteadyBeat()
        {
            // Ten minutes of a level always over the trigger and always at the peak:
            // the band pulses back to back, every one stirs, and the accent fires only
            // as often as the rest allows.
            var pulses = NewPulses();
            var trace = Run(pulses, 600, ratio: 1.5f);
            int stirs = 0;
            var accents = new List<double>();
            for (int i = 1; i < trace.Count; i++)
            {
                if (trace[i - 1].Turbulence < 1e-4f && trace[i].Turbulence > 1e-4f) stirs++;
                if (trace[i - 1].Pulse <= 0.99f && trace[i].Pulse > 0.99f) accents.Add(i * Hop);
            }
            Assert.Equal((int)(600 / Settings.PulseAccentSeconds) + 1, accents.Count);
            Assert.True(stirs > 10 * accents.Count, $"stirs {stirs}, accents {accents.Count}");
            var gaps = accents.Zip(accents.Skip(1), (a, b) => b - a);
            Assert.All(gaps, g => Assert.InRange(g, Settings.PulseAccentSeconds - Hop, Settings.PulseAccentSeconds + 2 * Hop));
        }

        [Fact]
        public void TheBaseline_FollowsTheBandsLevelAgainstItsAverage_Smoothly()
        {
            var pulses = NewPulses();
            float quiet = Baseline * (float)InjectionPulses.BaselineQuietFraction;
            float loud = Baseline * (float)InjectionPulses.BaselineLoudFraction;

            // A steady level IS the average: the baseline sits at its nominal value.
            var steady = Run(pulses, 30, ratio: 0f, level: Loud * 0.5f);
            Assert.Equal(Baseline, steady[^1].Pulse, 3);
            // Twice the average: the baseline about doubles, and quickly (the average
            // itself has started to climb by then, so not quite).
            var louder = Run(pulses, 2, ratio: 0f, level: Loud);
            Assert.InRange(louder[^1].Pulse, loud * 0.9f, loud);
            // Silence: down to the quiet fraction.
            var silence = Run(pulses, 15, ratio: 0f, level: 0f);
            Assert.Equal(quiet, silence[^1].Pulse, 3);

            // Each band follows its own level.
            var levels = new float[InjectionPulses.BandCount];
            levels[0] = Loud * 0.5f;
            for (int i = 0; i < 3000; i++) pulses.Advance(Ratios(0f), levels, Hop);
            Assert.True(pulses.Pulses[0] > Baseline * 0.9f);
            Assert.Equal(quiet, pulses.Pulses[1], 3);

            // Never a step, up or down.
            var trace = Run(pulses, 5, ratio: 0f, level: Loud).Concat(Run(pulses, 5, ratio: 0f, level: 0f)).ToList();
            float largestStep = 0f;
            for (int i = 1; i < trace.Count; i++) largestStep = Math.Max(largestStep, Math.Abs(trace[i].Pulse - trace[i - 1].Pulse));
            Assert.True(largestStep < 0.01f);
        }

        [Fact]
        public void APadPress_FloodsEveryTime_WhateverTheAudio_AndLeavesTheAccentAlone()
        {
            var pulses = NewPulses();
            // Silence, and a pad press: a full envelope of dye, and the accent is untouched.
            pulses.Advance(Ratios(0f), Ratios(0f), Hop, pressedDiscs: 1);
            var trace = Run(pulses, InjectionPulses.AttackSeconds + 0.1, ratio: 0f, level: 0f);
            Assert.Equal(1f, trace.Max(s => s.Pulse), 2);
            Assert.True(trace.Max(s => s.Turbulence) > 0.99f);              // it stirs too
            Assert.True(pulses.AccentReady);

            // Straight after it has run out, another press floods again — there is no rest.
            Run(pulses, InjectionPulses.PulseSeconds, ratio: 0f, level: 0f);
            Assert.True(NoAccent(Peak(pulses)));
            pulses.Advance(Ratios(0f), Ratios(0f), Hop, pressedDiscs: 1);
            var again = Run(pulses, InjectionPulses.AttackSeconds + 0.1, ratio: 0f, level: 0f);
            Assert.Equal(1f, again.Max(s => s.Pulse), 2);

            // And a press while the audio's accent is resting still floods.
            Run(pulses, InjectionPulses.PulseSeconds, ratio: 0f, level: 0f);
            pulses.Advance(Ratios(2f), Ratios(Loud), Hop);                   // the first hit: an accent
            Run(pulses, InjectionPulses.PulseSeconds, ratio: 0f, level: 0f);
            Assert.False(pulses.AccentReady);
            pulses.Advance(Ratios(0f), Ratios(0f), Hop, pressedDiscs: 1);
            var resting = Run(pulses, InjectionPulses.AttackSeconds + 0.1, ratio: 0f, level: 0f);
            Assert.Equal(1f, resting.Max(s => s.Pulse), 2);
        }

        [Fact]
        public void APress_IsABurst_AndAPressDuringAFlood_SnapsItBackToTheTop()
        {
            var pulses = NewPulses();
            // Full on the very next hop: no ramp into the colour.
            pulses.Advance(Ratios(0f), Ratios(0f), Hop, pressedDiscs: 1);
            pulses.Advance(Ratios(0f), Ratios(0f), Hop);
            Assert.True(Peak(pulses) > 0.95f, $"after one hop: {Peak(pulses)}");

            // Press again half way down the release: straight back to the top, for a whole hold.
            Run(pulses, InjectionPulses.PressAttackSeconds + InjectionPulses.HoldSeconds + InjectionPulses.ReleaseSeconds * 0.5, ratio: 0f, level: 0f);
            Assert.InRange(Peak(pulses), 0.3f, 0.8f);
            pulses.Advance(Ratios(0f), Ratios(0f), Hop, pressedDiscs: 1);
            pulses.Advance(Ratios(0f), Ratios(0f), Hop);
            Assert.True(Peak(pulses) > 0.95f);
            var held = Run(pulses, InjectionPulses.HoldSeconds - 0.05, ratio: 0f, level: 0f);
            Assert.True(held.All(s => s.Pulse > 0.99f));
            // ... and then it releases as before.
            var release = Run(pulses, InjectionPulses.ReleaseSeconds + 0.1, ratio: 0f, level: 0f);
            Assert.True(NoAccent(release[^1].Pulse));
        }

        [Fact]
        public void AHeldPad_InjectsAtFull_AfterTheDelay_AndEasesOutOnRelease()
        {
            var pulses = NewPulses();
            // Spend the accent first so the press itself is not what injects.
            pulses.Advance(Ratios(0f), Ratios(0f), Hop, pressedDiscs: 1);
            Run(pulses, InjectionPulses.PulseSeconds + 0.1, ratio: 0f, level: 0f);
            float quiet = Baseline * (float)InjectionPulses.BaselineQuietFraction;
            Assert.Equal(quiet, pulses.Pulses[0], 3);

            var trace = new List<float>();
            int hops = (int)Math.Round(3.0 / Hop);
            for (int i = 0; i < hops; i++)
            {
                pulses.Advance(Ratios(0f), Ratios(0f), Hop, heldDiscs: 1);
                trace.Add(Peak(pulses));
            }
            int hopsBeforeDelay = (int)Math.Round(InjectionPulses.HoldDelaySeconds * 0.5 / Hop);
            Assert.Equal(quiet, trace[hopsBeforeDelay], 3);               // nothing yet
            Assert.Equal((float)InjectionPulses.HoldLevel, trace[^1], 2);  // full, held
            Assert.Equal(1, pulses.Pulses.ToArray().Count(p => p > 0.99f));   // on one disc at a time

            // Smooth on the way up: no hop steps more than the ramp allows ...
            float largestStep = 0f;
            for (int i = 1; i < trace.Count; i++) largestStep = Math.Max(largestStep, Math.Abs(trace[i] - trace[i - 1]));
            Assert.True(largestStep < 1 - Math.Exp(-Hop / InjectionPulses.HoldRampSeconds) + 1e-3);

            // ... and released, it eases back to the baseline.
            var after = Run(pulses, 2, ratio: 0f, level: 0f);
            Assert.True(after[0].Pulse > quiet + 0.1f);
            Assert.Equal(quiet, after[^1].Pulse, 3);

            // An accent running on top is not reduced to the hold level.
            pulses.Advance(Ratios(0f), Ratios(0f), Hop, heldDiscs: 1);
            Run(pulses, Settings.PulseAccentSeconds, ratio: 0f, level: 0f);
            int hopsToMidHold = (int)Math.Round((InjectionPulses.AttackSeconds + InjectionPulses.HoldSeconds * 0.5) / Hop);
            for (int i = 0; i < hopsToMidHold; i++) pulses.Advance(Ratios(0f), Ratios(0f), Hop, pressedDiscs: i == 0 ? 1 : 0, heldDiscs: 1);
            Assert.True(Peak(pulses) > 0.99f);
        }

        [Fact]
        public void EachPad_FloodsItsOwnDisc_AndTwoHeldFloodTwo()
        {
            var pulses = NewPulses();
            // Pad on disc 2 pressed: disc 2 floods, the others sit at the baseline.
            pulses.Advance(Ratios(0f), Ratios(0f), Hop, pressedDiscs: 1 << 2);
            pulses.Advance(Ratios(0f), Ratios(0f), Hop);
            AssertFloodingDiscs(pulses, 1 << 2);
            // Another pad, on disc 4, while disc 2's flood runs: both.
            pulses.Advance(Ratios(0f), Ratios(0f), Hop, pressedDiscs: 1 << 4);
            pulses.Advance(Ratios(0f), Ratios(0f), Hop);
            AssertFloodingDiscs(pulses, (1 << 2) | (1 << 4));
            // Held: each held disc at full after the delay, independently of the presses.
            Run(pulses, InjectionPulses.PressSeconds + 1, ratio: 0f, level: 0f);
            AssertFloodingDiscs(pulses, 0);
            for (int i = 0; i < (int)Math.Round(1.0 / Hop); i++)
            {
                pulses.Advance(Ratios(0f), Ratios(0f), Hop, heldDiscs: (1 << 1) | (1 << 3));
            }
            AssertFloodingDiscs(pulses, (1 << 1) | (1 << 3));
        }

        private static void AssertFloodingDiscs(InjectionPulses pulses, int mask)
        {
            var p = pulses.Pulses.ToArray();
            for (int band = 0; band < InjectionPulses.BandCount; band++)
            {
                if ((mask & (1 << band)) != 0) Assert.True(p[band] > 0.95f, $"disc {band} should flood: {p[band]}");
                else Assert.True(NoAccent(p[band]), $"disc {band} should not: {p[band]}");
            }
        }

        [Fact]
        public void TheFirstHitAfterStart_IsFull()
        {
            // Before anything louder has been heard, a hit is its own reference, and the
            // band has not spent its accent.
            var pulses = NewPulses();
            pulses.Advance(Ratios(2f), Ratios(0.05f), Hop);
            var trace = Run(pulses, InjectionPulses.AttackSeconds + 0.1, ratio: 0f, level: 0f);
            Assert.Equal(1f, trace.Max(s => s.Pulse), 2);
        }

        [Fact]
        public void TheEnvelope_IsSmooth()
        {
            var pulses = NewPulses();
            var trace = Run(pulses, 6, ratio: 2f);
            float largestStep = 0f;
            for (int i = 1; i < trace.Count; i++)
            {
                largestStep = Math.Max(largestStep, Math.Abs(trace[i].Pulse - trace[i - 1].Pulse));
                largestStep = Math.Max(largestStep, Math.Abs(trace[i].Turbulence - trace[i - 1].Turbulence));
            }
            // A raised cosine over the attack moves at most ~π/2 · hop/attack per hop.
            Assert.InRange(largestStep, 0.01f, 0.25f);
        }

        [Fact]
        public void GarbageIn_StaysFinite()
        {
            var pulses = NewPulses();
            pulses.Advance(new[] { float.NaN, float.PositiveInfinity, -1f, 2f, 1f },
                           new[] { float.NaN, float.PositiveInfinity, -1f, 2f, 1f }, Hop);
            pulses.Advance(Ratios(2f), Ratios(Loud), double.NaN);
            pulses.Advance(Ratios(2f), Ratios(Loud), 0);
            foreach (float p in pulses.Pulses.ToArray())
            {
                Assert.True(float.IsFinite(p));
                Assert.InRange(p, 0f, 1f);
            }
            Assert.InRange(pulses.Turbulence, 0f, 1f);
        }
    }
}
