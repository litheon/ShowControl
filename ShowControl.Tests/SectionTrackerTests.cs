using System;
using System.Collections.Generic;
using System.Linq;
using ShowControl.AudioAnalysis;
using Xunit;

namespace ShowControl.Tests
{
    public class SectionTrackerTests
    {
        private const double Hop = 512.0 / 48_000;

        private sealed class Harness
        {
            public SectionTracker Tracker { get; }
            public double Time { get; private set; }
            public List<(double Time, Section Section, float Tension, float Motion, float Spread)> Trace { get; } = new();

            public Harness(SectionSettings? settings = null, Func<double, double>? nextDownbeat = null)
            {
                Tracker = new SectionTracker(
                    settings ?? new SectionSettings(),
                    nextDownbeat ?? (t => t + 0.4));
            }

            /// <summary>Feeds features for a duration; each feature is a function of the
            /// local 0..1 progress through this segment.</summary>
            public void Feed(double seconds,
                Func<double, float> rms,
                Func<double, float>? centroid = null,
                Func<double, float>? onsetRate = null,
                Func<double, float>? highBand = null)
            {
                double start = Time;
                while (Time < start + seconds)
                {
                    double p = (Time - start) / seconds;
                    var f = new SectionFeatures(
                        Time,
                        rms(p),
                        Flux: 0.01f,
                        CentroidHz: centroid?.Invoke(p) ?? 2000f,
                        OnsetRate: onsetRate?.Invoke(p) ?? 2f,
                        HighBand: highBand?.Invoke(p) ?? 0.2f,
                        TempoConfidence: 0.8f);
                    Tracker.Advance(f, Hop);
                    Trace.Add((Time, Tracker.Section, Tracker.Tension, Tracker.Motion, Tracker.Spread));
                    Time += Hop;
                }
            }

            /// <summary>Steady groove long enough to settle the trend statistics.</summary>
            public void Groove(double seconds = 12) => Feed(seconds, _ => 0.3f);

            /// <summary>A textbook build: level, brightness, onset density and high band all
            /// rising over the segment.</summary>
            public void Build(double seconds = 10) => Feed(seconds,
                p => 0.25f + 0.35f * (float)p,
                p => 1800f + 2600f * (float)p,
                p => 2f + 7f * (float)p,
                p => 0.15f + 0.5f * (float)p);
        }

        [Fact]
        public void SteadyGroove_StaysSteadyAndNeutral()
        {
            var h = new Harness();
            h.Groove(20);

            Assert.Equal(Section.Steady, h.Tracker.Section);
            Assert.True(h.Tracker.Tension < 0.25, $"tension {h.Tracker.Tension}");
            Assert.InRange(h.Tracker.Motion, 0.8f, 1.15f);
            Assert.InRange(h.Tracker.Spread, 0.35f, 0.6f);
        }

        [Fact]
        public void RisingTrends_AccumulateIntoABuild()
        {
            var h = new Harness();
            h.Groove();
            h.Build();

            Assert.Equal(Section.Build, h.Tracker.Section);
            Assert.True(h.Tracker.Tension > 0.5, $"tension {h.Tracker.Tension}");
            Assert.True(h.Tracker.Spread < 0.4, $"spread {h.Tracker.Spread}");   // collapses in a build
            Assert.True(h.Tracker.Motion < 1.0, $"motion {h.Tracker.Motion}");
        }

        [Fact]
        public void CollapseAfterConfidentBuild_CommitsGapAndHoldsTensionAtOne()
        {
            var h = new Harness();
            h.Groove();
            h.Build();
            h.Feed(0.8, _ => 0.01f); // broadband cut

            Assert.Equal(Section.Gap, h.Tracker.Section);
            Assert.Equal(1f, h.Tracker.Tension);
            Assert.True(h.Tracker.Motion < 0.3, $"motion {h.Tracker.Motion}"); // the freeze

            // Held, not decaying: every gap sample after commit keeps tension at 1.
            var gapSamples = h.Trace.Where(s => s.Section == Section.Gap).ToList();
            Assert.All(gapSamples, s => Assert.Equal(1f, s.Tension));
        }

        [Fact]
        public void EnergyReturn_FiresTheDrop_SnapsTensionAndSpikesMotion()
        {
            var h = new Harness();
            h.Groove();
            h.Build();
            h.Feed(0.3, _ => 0.01f);   // gap
            h.Feed(0.05, _ => 0.5f);   // the hit

            Assert.Equal(Section.Drop, h.Tracker.Section);
            Assert.Equal(0f, h.Tracker.Tension);
            Assert.True(h.Tracker.Motion > 1.8f, $"motion {h.Tracker.Motion}");
            Assert.True(h.Tracker.Spread > 0.75f, $"spread {h.Tracker.Spread}");

            // The pulse settles back toward the groove.
            h.Feed(5, _ => 0.4f);
            Assert.Equal(Section.Steady, h.Tracker.Section);
            Assert.InRange(h.Tracker.Motion, 0.7f, 1.3f);
            Assert.InRange(h.Tracker.Spread, 0.35f, 0.65f);
        }

        [Fact]
        public void MissedDrop_LeaksTensionGracefully_NeverSnaps()
        {
            // nextDownbeat 0.4s out, grace 1.2s → the gap gives up ~1.6s in.
            var h = new Harness();
            h.Groove();
            h.Build();
            h.Feed(4, _ => 0.005f); // collapse that never resolves

            Assert.Equal(Section.Steady, h.Tracker.Section);
            Assert.True(h.Tracker.Tension < 0.9f, "tension should be leaking");
            Assert.True(h.Tracker.Tension > 0.05f, "leak, not snap");

            // Cosmetic degradation: after the gap ends, tension only ever eases down.
            var afterGap = h.Trace.SkipWhile(s => s.Section != Section.Gap)
                                  .SkipWhile(s => s.Section == Section.Gap)
                                  .ToList();
            Assert.NotEmpty(afterGap);
            for (int i = 1; i < afterGap.Count; i++)
            {
                double delta = afterGap[i].Tension - afterGap[i - 1].Tension;
                Assert.True(delta <= 0.001, $"tension rose during leak at {afterGap[i].Time}");
                Assert.True(delta > -0.05, $"tension snapped during leak at {afterGap[i].Time}");
            }
        }

        [Fact]
        public void AnnouncedBuildAndDrop_ScheduleTensionAndFireEarlyByLead()
        {
            var settings = new SectionSettings { LeadSeconds = 0.1 };
            var h = new Harness(settings);
            h.Tracker.SetAnnouncedSections(new[]
            {
                new SectionMarker(5.0, SectionKind.BuildUp, 1f),
                new SectionMarker(15.0, SectionKind.Drop, 1f),
            });

            // Flat groove throughout: everything below must come from the markers alone.
            h.Feed(14.2, _ => 0.3f);
            Assert.True(h.Tracker.Tension > 0.85f, $"tension {h.Tracker.Tension} approaching the announced hit");
            Assert.True(h.Tracker.Section is Section.Build or Section.Gap);

            h.Feed(1.0, _ => 0.3f); // crosses 15.0 - lead
            var drop = h.Trace.FirstOrDefault(s => s.Section == Section.Drop);
            Assert.NotEqual(default, drop);
            Assert.InRange(drop.Time, 14.85, 15.0); // fired early by the lead, not at 15.0
            Assert.Equal(0f, drop.Tension);
            Assert.True(drop.Motion > 1.8f);
        }

        [Fact]
        public void OutputsAlwaysBounded()
        {
            var h = new Harness();
            h.Groove(2);
            h.Build(3);
            h.Feed(1, _ => 0.001f);
            h.Feed(2, _ => 0.9f);
            h.Feed(1, _ => float.NaN); // hostile

            foreach (var s in h.Trace)
            {
                Assert.InRange(s.Tension, 0f, 1f);
                Assert.InRange(s.Motion, 0f, 3f);
                Assert.InRange(s.Spread, 0f, 1f);
            }
        }
    }
}
