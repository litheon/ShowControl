using System;
using System.Linq;
using ShowControl.PixelMapping;
using Xunit;

namespace ShowControl.Tests
{
    public class PixelOutputOverrideTests
    {
        private static (PixelMapLayout Layout, FixtureDefinition A, FixtureDefinition B) TwoStrips()
        {
            var a = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 6);
            var b = PixelMappingTestData.Strip("B", 50f, 0f, pixelCount: 4, startChannel: 18);
            var map = PixelMappingTestData.MapOf(a, b);
            return (PixelMapLayoutBuilder.Build(map, new TestPixelTypes()), a, b);
        }

        [Fact]
        public void NoRequest_ReplacesNothing()
        {
            var (layout, _, _) = TwoStrips();
            var over = PixelOutputOverride.Compile(layout, PixelOutputOverrideRequest.Nothing);
            Assert.False(over.IsActive);
            Assert.All(Enumerable.Range(0, over.PixelCount), i => Assert.False(over.Replaces(i)));
        }

        [Fact]
        public void Solo_LeavesTheSoloedStripToTheAnimation_AndBlacksTheRest()
        {
            var (layout, a, b) = TwoStrips();
            var over = PixelOutputOverride.Compile(layout, new PixelOutputOverrideRequest(PixelTestPattern.None, b.Id));
            Assert.True(over.IsActive);
            for (int i = 0; i < 6; i++)
            {
                Assert.True(over.Replaces(i));                        // A: darkened
                Assert.Equal(new RgbSample(0, 0, 0), over.ColorOf(i, 0));
            }
            for (int i = 6; i < 10; i++)
            {
                Assert.False(over.Replaces(i));                       // B: shows through
            }
        }

        [Fact]
        public void APattern_ReplacesEveryPixel_AndRespectsTheSolo()
        {
            var (layout, a, b) = TwoStrips();
            var over = PixelOutputOverride.Compile(layout, new PixelOutputOverrideRequest(PixelTestPattern.White, a.Id));
            Assert.All(Enumerable.Range(0, 10), i => Assert.True(over.Replaces(i)));
            Assert.All(Enumerable.Range(0, 6), i => Assert.Equal(new RgbSample(255, 255, 255), over.ColorOf(i, 0)));
            Assert.All(Enumerable.Range(6, 4), i => Assert.Equal(new RgbSample(0, 0, 0), over.ColorOf(i, 0)));
        }

        [Fact]
        public void Rainbow_RunsRedToRedAlongEachStrip()
        {
            var (layout, _, _) = TwoStrips();
            var over = PixelOutputOverride.Compile(layout, new PixelOutputOverrideRequest(PixelTestPattern.Rainbow, null));
            Assert.Equal(new RgbSample(255, 0, 0), over.ColorOf(0, 0));         // A pixel 0
            Assert.Equal(new RgbSample(255, 0, 0), over.ColorOf(6, 0));         // B pixel 0
            Assert.Equal(new RgbSample(0, 255, 255), over.ColorOf(3, 0));       // A half way: cyan
            Assert.NotEqual(over.ColorOf(1, 0), over.ColorOf(2, 0));
        }

        [Fact]
        public void FixtureColors_GiveEachStripOneColour_DifferentFromTheNext()
        {
            var (layout, _, _) = TwoStrips();
            var over = PixelOutputOverride.Compile(layout, new PixelOutputOverrideRequest(PixelTestPattern.FixtureColors, null));
            Assert.All(Enumerable.Range(0, 6), i => Assert.Equal(over.ColorOf(0, 0), over.ColorOf(i, 0)));
            Assert.All(Enumerable.Range(6, 4), i => Assert.Equal(over.ColorOf(6, 0), over.ColorOf(i, 0)));
            Assert.NotEqual(over.ColorOf(0, 0), over.ColorOf(6, 0));
        }

        [Fact]
        public void Chase_RunsAHeadFromPixelZeroToTheEnd_WithATrailBehindIt()
        {
            var (layout, _, _) = TwoStrips();
            var over = PixelOutputOverride.Compile(layout, new PixelOutputOverrideRequest(PixelTestPattern.Chase, null));
            // At t=0 the head is at pixel 0 of every strip, nothing ahead of it is lit.
            Assert.Equal(new RgbSample(255, 255, 255), over.ColorOf(0, 0));
            Assert.Equal(new RgbSample(255, 255, 255), over.ColorOf(6, 0));
            Assert.True(over.ColorOf(1, 0).R < 10);
            // A little later it has moved along A and left a dimmer trail behind.
            double t = 3 / 40.0 + 1e-6;                                         // head at pixel 3
            Assert.Equal(new RgbSample(255, 255, 255), over.ColorOf(3, t));
            Assert.InRange(over.ColorOf(2, t).R, 100, 254);
            Assert.True(over.ColorOf(2, t).R > over.ColorOf(1, t).R);
            Assert.True(over.ColorOf(4, t).R < 10);
            // It comes round again.
            Assert.Equal(new RgbSample(255, 255, 255), over.ColorOf(0, 0));
            Assert.Equal(over.ColorOf(0, 0), over.ColorOf(0, (6 + 8 + 12) / 40.0 + 1e-6));
        }

        [Fact]
        public void ADisabledFixture_IsNotInTheFrame_AndTheOrdinalsSkipIt()
        {
            var a = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 2);
            var off = PixelMappingTestData.Strip("Off", 10f, 0f, pixelCount: 2, startChannel: 6, enabled: false);
            var b = PixelMappingTestData.Strip("B", 50f, 0f, pixelCount: 2, startChannel: 12);
            var map = PixelMappingTestData.MapOf(a, off, b);
            var layout = PixelMapLayoutBuilder.Build(map, new TestPixelTypes());
            var request = new PixelOutputOverrideRequest(PixelTestPattern.FixtureColors, null);
            var over = PixelOutputOverride.Compile(layout, request);
            Assert.Equal(4, over.PixelCount);
            // The preview's fixture-based lookup agrees with the frame's, counting enabled fixtures only.
            Assert.Equal(over.ColorOf(2, 0), PixelOutputOverride.ColorFor(request, b, 1, 0, 0));
            Assert.Equal(over.ColorOf(0, 0), PixelOutputOverride.ColorFor(request, a, 0, 0, 0));
        }

        [Fact]
        public void BlankedPixels_StayBlack_InEveryPattern()
        {
            var a = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 6);
            a.BlankedPixels.Add(new PixelRange(4, 2));
            var layout = PixelMapLayoutBuilder.Build(PixelMappingTestData.MapOf(a), new TestPixelTypes());
            foreach (var pattern in new[] { PixelTestPattern.Chase, PixelTestPattern.Rainbow, PixelTestPattern.FixtureColors, PixelTestPattern.White, PixelTestPattern.Diagonals })
            {
                var request = new PixelOutputOverrideRequest(pattern, null);
                var over = PixelOutputOverride.Compile(layout, request);
                for (double t = 0; t < 1; t += 0.1)
                {
                    Assert.Equal(new RgbSample(0, 0, 0), over.ColorOf(4, t));
                    Assert.Equal(new RgbSample(0, 0, 0), over.ColorOf(5, t));
                    Assert.Equal(new RgbSample(0, 0, 0), PixelOutputOverride.ColorFor(request, a, 0, 5, t));
                }
                Assert.NotEqual(new RgbSample(0, 0, 0), over.ColorOf(0, 0));   // the live pixels are lit
            }
        }

        [Fact]
        public void Diagonals_AreDrawnInImageSpace_SoAStripBelowShowsTheLineShiftedByItsOffset()
        {
            // Two 1-unit-spaced strips, the lower one 10 units down: a 45° line that crosses the
            // top strip at x crosses the lower one at x + 10, whatever pixel that happens to be.
            var top = PixelMappingTestData.Strip("Top", 0f, 0f, pixelCount: 120);
            var below = PixelMappingTestData.Strip("Below", 0f, 10f, pixelCount: 120, startChannel: 360);
            var layout = PixelMapLayoutBuilder.Build(PixelMappingTestData.MapOf(top, below), new TestPixelTypes());
            var request = new PixelOutputOverrideRequest(PixelTestPattern.Diagonals, null);
            var over = PixelOutputOverride.Compile(layout, request);
            var dark = new RgbSample(6, 6, 6);

            // The top strip: a line at x = 0, dark after its width, the next line a period on.
            var first = over.ColorOf(0, 0);
            Assert.NotEqual(dark, first);
            Assert.Equal(first, over.ColorOf((int)PixelOutputOverride.DiagonalWidth - 1, 0));
            Assert.Equal(dark, over.ColorOf((int)PixelOutputOverride.DiagonalWidth, 0));
            var second = over.ColorOf((int)PixelOutputOverride.DiagonalPeriod, 0);
            Assert.NotEqual(dark, second);
            Assert.NotEqual(first, second);                                  // the next line has its own colour

            // The strip below: the same first line arrives 10 pixels later, and nothing before it.
            Assert.All(Enumerable.Range(0, 10), x => Assert.Equal(dark, over.ColorOf(120 + x, 0)));
            Assert.Equal(first, over.ColorOf(120 + 10, 0));
            Assert.Equal(second, over.ColorOf(120 + 10 + (int)PixelOutputOverride.DiagonalPeriod, 0));

            // The preview's fixture-based lookup agrees, and the pattern does not move.
            Assert.Equal(first, PixelOutputOverride.ColorFor(request, below, 1, 10, 0));
            Assert.Equal(first, over.ColorOf(130, 60));
        }

        [Fact]
        public void ColorFor_ReturnsNull_WhereTheAnimationShowsThrough()
        {
            var a = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 2);
            var b = PixelMappingTestData.Strip("B", 50f, 0f, pixelCount: 2, startChannel: 6);
            Assert.Null(PixelOutputOverride.ColorFor(PixelOutputOverrideRequest.Nothing, a, 0, 0, 0));
            var solo = new PixelOutputOverrideRequest(PixelTestPattern.None, a.Id);
            Assert.Null(PixelOutputOverride.ColorFor(solo, a, 0, 1, 0));
            Assert.Equal(new RgbSample(0, 0, 0), PixelOutputOverride.ColorFor(solo, b, 1, 0, 0));
        }
    }
}
