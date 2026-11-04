using System;
using ShowControl.AudioAnalysis;
using Xunit;

namespace ShowControl.Tests
{
    public class InjectionPaletteTests
    {
        private const double Hop = 512.0 / 48_000;

        /// <summary>Shortest distance between two hues around the wheel, in turns.</summary>
        private static double HueDistance(double a, double b)
        {
            double d = Math.Abs(a - b) % 1.0;
            return Math.Min(d, 1 - d);
        }

        private static void Run(InjectionPalette palette, double seconds, double energy, double tension)
        {
            int hops = (int)Math.Round(seconds / Hop);
            for (int i = 0; i < hops; i++)
            {
                palette.Advance(Hop, energy, tension, false);
            }
        }

        private static int FirstBandOf(int family)
        {
            int band = 0;
            while (InjectionPalette.FamilyOf(band) != family)
            {
                band++;
            }
            return band;
        }

        [Fact]
        public void WithNoTension_TheThreeFamiliesFormATriad()
        {
            var palette = new InjectionPalette();
            Run(palette, 1, energy: 0.5, tension: 0);

            double low = palette.Hues[FirstBandOf(0)];
            double mid = palette.Hues[FirstBandOf(1)];
            double high = palette.Hues[FirstBandOf(2)];
            // A third of the wheel apart, give or take the shade inside a family.
            double shade = InjectionPalette.FamilyShadeWidth / 2;
            Assert.InRange(HueDistance(low, mid), 1.0 / 3 - shade, 1.0 / 3 + shade);
            Assert.InRange(HueDistance(mid, high), 1.0 / 3 - shade, 1.0 / 3 + shade);
            Assert.InRange(HueDistance(high, low), 1.0 / 3 - shade, 1.0 / 3 + shade);
        }

        [Fact]
        public void BandsOfOneFamily_AreShadesOfOneColor()
        {
            var palette = new InjectionPalette();
            Run(palette, 1, energy: 0.5, tension: 0);

            for (int band = 0; band < InjectionHistory.BandCount; band++)
            {
                double anchor = palette.Hues[FirstBandOf(InjectionPalette.FamilyOf(band))];
                Assert.True(HueDistance(palette.Hues[band], anchor) <= InjectionPalette.FamilyShadeWidth);
            }
        }

        [Fact]
        public void FullTension_ClosesThePaletteOntoTheKey()
        {
            var palette = new InjectionPalette();
            Run(palette, 1, energy: 0.5, tension: 1);

            foreach (float hue in palette.Hues.ToArray())
            {
                Assert.True(HueDistance(hue, palette.KeyHue) <= InjectionPalette.FamilyShadeWidth / 2 + 1e-4);
            }
        }

        [Fact]
        public void ADrop_TurnsTheKeyByTheGoldenAngle()
        {
            var palette = new InjectionPalette();
            float before = palette.KeyHue;

            palette.Advance(Hop, 0, 0, dropFired: true);
            // The turn is eased: one hop in, most of it is still to come.
            Assert.True(HueDistance(palette.KeyHue, before) < 0.05);

            Run(palette, 3, energy: 0, tension: 0);
            Assert.Equal(InjectionPalette.DropTurn, HueDistance(palette.KeyHue, before), precision: 3);
        }

        [Fact]
        public void TheKeyDriftsWithEnergy_AndStandsStillInSilence()
        {
            var silent = new InjectionPalette();
            float start = silent.KeyHue;
            Run(silent, 30, energy: 0, tension: 0);
            Assert.Equal(start, silent.KeyHue, precision: 5);

            var loud = new InjectionPalette();
            Run(loud, 30, energy: 1, tension: 0);
            Assert.Equal(30 * InjectionPalette.DriftPerSecond, HueDistance(loud.KeyHue, start), precision: 3);
        }

        [Fact]
        public void EachDrop_SwitchesBetweenPlainAndMarbled_Gradually()
        {
            var palette = new InjectionPalette();
            Run(palette, 1, energy: 0.5, tension: 0);
            Assert.InRange(palette.Marble, 0.99f, 1f);

            palette.Advance(Hop, 0.5, 0, dropFired: true);
            Assert.True(palette.Marble > 0.9f);          // eased, not cut
            Run(palette, 15, energy: 0.5, tension: 0);
            Assert.InRange(palette.Marble, 0f, 0.01f);

            palette.Advance(Hop, 0.5, 0, dropFired: true);
            Run(palette, 15, energy: 0.5, tension: 0);
            Assert.InRange(palette.Marble, 0.99f, 1f);
        }

        [Fact]
        public void MarbleAlways_HoldsTheMarblingOnThroughDrops()
        {
            var palette = new InjectionPalette { MarbleAlways = true };
            for (int drop = 0; drop < 4; drop++)
            {
                palette.Advance(Hop, 0.8, 0.2, dropFired: true);
                for (int i = 0; i < 1000; i++) palette.Advance(Hop, 0.8, 0.2, false);
                Assert.Equal(1f, palette.Marble, 3);
            }
        }

        [Fact]
        public void Hues_AreAlwaysOnTheWheel()
        {
            var palette = new InjectionPalette();
            for (int i = 0; i < 20_000; i++)
            {
                palette.Advance(Hop, 1, (i % 700) / 700.0, i % 900 == 0);
                foreach (float hue in palette.Hues.ToArray())
                {
                    Assert.InRange(hue, 0f, 1f);
                }
            }

            palette.Advance(double.NaN, double.NaN, double.NaN, true);
            foreach (float hue in palette.Hues.ToArray())
            {
                Assert.InRange(hue, 0f, 1f);
            }
        }
    }
}
