using System;
using ShowControl.AudioAnalysis;
using Xunit;

namespace ShowControl.Tests
{
    public class InjectionHistoryTests
    {
        private const double Hop = 512.0 / 48_000;

        /// <summary>A spectrum with one loud bin; bin 5 is about 117 Hz, inside the bass the history follows.</summary>
        private static float[] Tone(int bin, float level = 0.05f)
        {
            var magnitudes = new float[1024];
            magnitudes[bin] = level;
            return magnitudes;
        }

        private static void Run(InjectionHistory history, float[] magnitudes, double seconds)
        {
            int hops = (int)Math.Round(seconds / Hop);
            for (int i = 0; i < hops; i++)
            {
                history.Advance(magnitudes, Hop);
            }
        }

        private static float Depth(InjectionHistory history, int band) =>
            history.Sites[InjectionHistory.BandCount + band];

        private static int MovedBand(InjectionHistory history)
        {
            int moved = -1;
            for (int band = 0; band < InjectionHistory.BandCount; band++)
            {
                if (Math.Abs(history.Sites[band] - InjectionHistory.HomeSite(band)) > 1e-4f)
                {
                    Assert.Equal(-1, moved);
                    moved = band;
                }
            }
            return moved;
        }

        [Fact]
        public void WithNoHistory_EveryBandIsHome_InFrequencyOrder()
        {
            var history = new InjectionHistory(new AnalysisSettings());
            Run(history, new float[1024], 5);

            for (int band = 0; band < InjectionHistory.BandCount; band++)
            {
                Assert.Equal(InjectionHistory.HomeSite(band), history.Sites[band], precision: 5);
                Assert.Equal(0f, Depth(history, band), precision: 5);
            }
        }

        [Fact]
        public void AnActiveBand_LeavesTheTopEdge()
        {
            var history = new InjectionHistory(new AnalysisSettings());
            Run(history, Tone(5), 15);

            int band = MovedBand(history);
            Assert.True(Depth(history, band) > 0.05f, $"depth {Depth(history, band)}");
            for (int other = 0; other < InjectionHistory.BandCount; other++)
            {
                if (other != band)
                {
                    Assert.Equal(0f, Depth(history, other), precision: 5);
                }
            }
        }

        [Fact]
        public void DepthFollowsTheRecentWindow_AcrossFollowsTheWholeMinute()
        {
            // The tone stopped longer ago than the recent window but well inside the
            // minute: the band is back at the top and still displaced across.
            var history = new InjectionHistory(new AnalysisSettings());
            Run(history, Tone(5), 5);
            int band = MovedBand(history);
            // The recent window, plus time for the glide to settle.
            Run(history, new float[1024], InjectionHistory.RecentSeconds + 22);

            Assert.InRange(Depth(history, band), 0f, 0.005f);
            Assert.True(Math.Abs(history.Sites[band] - InjectionHistory.HomeSite(band)) > 0.02f);
        }

        [Fact]
        public void AnActiveBand_Travels_AndTheSilentOnesStayPut()
        {
            var history = new InjectionHistory(new AnalysisSettings());
            Run(history, Tone(5), 10);

            // Exactly one band has left home — the one holding the tone.
            Assert.NotEqual(-1, MovedBand(history));
        }

        [Fact]
        public void TheSiteDependsOnTheWholeMinute_NotOnThePresentFrame()
        {
            // Two histories ending on the same frame, one with ten seconds of the tone
            // behind it and one with forty: an average of the present would agree.
            var brief = new InjectionHistory(new AnalysisSettings());
            var lengthy = new InjectionHistory(new AnalysisSettings());
            Run(brief, Tone(5), 10);
            Run(lengthy, Tone(5), 40);

            int band = MovedBand(brief);
            Assert.True(Math.Abs(brief.Sites[band] - lengthy.Sites[band]) > 0.02f,
                $"brief {brief.Sites[band]} vs lengthy {lengthy.Sites[band]}");
        }

        [Fact]
        public void HistoryOlderThanTheWindow_IsForgotten()
        {
            var history = new InjectionHistory(new AnalysisSettings());
            Run(history, Tone(5), 20);
            int band = MovedBand(history);

            // The window, plus time for the glide to settle.
            Run(history, new float[1024], InjectionHistory.WindowSeconds + 60);

            Assert.Equal(InjectionHistory.HomeSite(band), history.Sites[band], precision: 4);
            Assert.Equal(0f, Depth(history, band), precision: 4);
        }

        [Fact]
        public void WhenEveryBandIsEquallyBusy_BothSidesKeepInjectors()
        {
            // Broadband material moves every band by the same amount — the case that
            // used to march the whole set to one side.
            var history = new InjectionHistory(new AnalysisSettings());
            var magnitudes = new float[1024];
            Array.Fill(magnitudes, 0.05f);

            int hops = (int)(120 / Hop);
            for (int i = 0; i < hops; i++)
            {
                history.Advance(magnitudes, Hop);
                int left = 0;
                for (int band = 0; band < InjectionHistory.BandCount; band++)
                {
                    if (history.Sites[band] < 0.5f)
                    {
                        left++;
                    }
                }
                Assert.InRange(left, 1, InjectionHistory.BandCount - 1);
            }
        }

        [Fact]
        public void Sites_MoveContinuously_AndStayOnTheEdge()
        {
            // Long enough to fold at the walls and start evicting: no hop may move a site
            // by more than the travel rates allow, and every coordinate stays inside 0..1.
            var history = new InjectionHistory(new AnalysisSettings());
            var magnitudes = Tone(5);
            var previous = history.Sites.ToArray();
            double maxStep = InjectionHistory.TravelPerActiveSecond * Hop * 1.01;
            double maxDescent = InjectionHistory.DescentPerActiveSecond * Hop * 1.01;

            int hops = (int)(150 / Hop);
            for (int i = 0; i < hops; i++)
            {
                // Pulse the tone so the window's content keeps changing after it fills.
                history.Advance((i / 400) % 3 == 0 ? new float[1024] : magnitudes, Hop);
                for (int band = 0; band < InjectionHistory.BandCount; band++)
                {
                    float site = history.Sites[band];
                    Assert.InRange(site, 0f, 1f);
                    Assert.True(Math.Abs(site - previous[band]) <= maxStep + 1e-6,
                        $"band {band} jumped {Math.Abs(site - previous[band])} at hop {i}");
                    previous[band] = site;

                    int d = InjectionHistory.BandCount + band;
                    float depth = history.Sites[d];
                    Assert.InRange(depth, 0f, 1f);
                    Assert.True(Math.Abs(depth - previous[d]) <= maxDescent + 1e-6,
                        $"band {band} depth jumped {Math.Abs(depth - previous[d])} at hop {i}");
                    previous[d] = depth;
                }
            }
        }

        [Fact]
        public void Engine_PublishesTheInjectionTableAlongsideTheSpectrum()
        {
            var engine = new AudioAnalysisEngine();
            int observedLength = 0;
            var first = new float[InjectionHistory.BandCount];
            var last = new float[InjectionHistory.BandCount];
            bool steady = true;
            bool anyDepth = false;
            long frames = 0;
            engine.SnapshotPublished += _ =>
            {
                var table = engine.CurrentInjectionTable;
                observedLength = table.Length;
                for (int band = 0; band < InjectionHistory.BandCount; band++)
                {
                    if (frames == 0)
                    {
                        first[band] = table[band];
                    }
                    else
                    {
                        // Across, every point moves the same way on every frame: nothing
                        // but the bias moves it, and no drop has switched the bias. The
                        // position wraps, so "moved right or stayed" is a step in [0, 0.5).
                        float step = (table[band] - last[band] + 1f) % 1f;
                        steady &= step < 0.5f;
                    }
                    last[band] = table[band];
                    anyDepth |= table[InjectionHistory.BandCount + band] > 1e-3f;
                }
                frames++;
            };

            var block = new float[48_000 * 20];
            for (int i = 0; i < block.Length; i++)
            {
                block[i] = (float)Math.Sin(2 * Math.PI * 120.0 * i / 48_000.0);
            }
            engine.Submit(block, 0);
            engine.ProcessPending();

            Assert.Equal(InjectionHistory.BandCount * 4 + 5, observedLength);
            Assert.True(steady);
            Assert.True((last[0] - first[0] + 1f) % 1f is > 0.01f and < 0.5f);
            Assert.True(anyDepth);
        }
    }
}
