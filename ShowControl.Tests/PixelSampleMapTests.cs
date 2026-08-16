using System;
using ShowControl.PixelMapping;
using Xunit;

namespace ShowControl.Tests
{
    public class PixelSampleMapTests
    {
        /// <summary>
        /// The gate for the cutover. The pixel map replaced the hand-written strip arithmetic in
        /// ShaderAnimation with no fallback path, so the new sampling has to reproduce the old one
        /// exactly or the rig changes the first time the app starts.
        /// </summary>
        [Fact]
        public void LegacySamplingReference_AndDefaultMapWithAllStripsEnabled_ProduceIdenticalPixels()
        {
            var image = PixelMappingTestData.SyntheticImage();
            var expected = PixelMappingTestData.LegacySample(
                image,
                PixelMappingTestData.ImageWidth,
                PixelMappingTestData.ImageHeight,
                PixelMappingTestData.LegacyPixelCount);

            var map = PixelMappingTestData.AllStripsEnabled();
            var layout = PixelMapLayoutBuilder.Build(map, new TestPixelTypes());
            var sampleMap = PixelSampleMapBuilder.Build(map, layout);

            Assert.Equal(PixelMappingTestData.LegacyPixelCount, sampleMap.PixelCount);

            var actual = new RgbSample[sampleMap.PixelCount];
            sampleMap.Sample(image, actual);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(
                    expected[i] == actual[i],
                    $"Pixel {i} (strip {i / 720}, position {i % 720}): legacy {expected[i]} vs mapped {actual[i]}");
            }
        }

        /// <summary>
        /// Only four strips were ever reaching a controller: the single live DdpDevice took
        /// targetVector[0..8639]. Those are the ones the shipped map enables, and they must line up
        /// with the front of the legacy frame.
        /// </summary>
        [Fact]
        public void ShippedDefaultMap_SamplesTheSamePixelsThatUsedToLeaveTheMachine()
        {
            var image = PixelMappingTestData.SyntheticImage();
            var legacy = PixelMappingTestData.LegacySample(
                image,
                PixelMappingTestData.ImageWidth,
                PixelMappingTestData.ImageHeight,
                PixelMappingTestData.LegacyPixelCount);

            var map = DefaultPixelMap.Load();
            var layout = PixelMapLayoutBuilder.Build(map, new TestPixelTypes());
            var sampleMap = PixelSampleMapBuilder.Build(map, layout);

            Assert.Equal(2880, sampleMap.PixelCount);

            var actual = new RgbSample[sampleMap.PixelCount];
            sampleMap.Sample(image, actual);

            for (int i = 0; i < actual.Length; i++)
            {
                Assert.True(legacy[i] == actual[i], $"Pixel {i}: legacy {legacy[i]} vs mapped {actual[i]}");
            }
        }

        [Fact]
        public void DefaultMap_PlacesStripKOnImageRow400Plus10K_AndBlanksTheLastTwelvePixels()
        {
            var map = PixelMappingTestData.AllStripsEnabled();
            var layout = PixelMapLayoutBuilder.Build(map, new TestPixelTypes());
            var sampleMap = PixelSampleMapBuilder.Build(map, layout);
            var offsets = sampleMap.ByteOffsets;

            for (int strip = 0; strip < PixelMappingTestData.StripCount; strip++)
            {
                int row = 400 + (10 * strip);
                int baseIndex = strip * PixelMappingTestData.PixelsPerStrip;

                for (int column = 0; column < 708; column++)
                {
                    int expected = ((row * PixelMappingTestData.ImageWidth) + column) * 4;
                    Assert.Equal(expected, offsets[baseIndex + column]);
                }

                for (int column = 708; column < PixelMappingTestData.PixelsPerStrip; column++)
                {
                    Assert.Equal(PixelSampleMap.Blank, offsets[baseIndex + column]);
                }
            }
        }

        [Fact]
        public void PixelDraggedOffTheImage_SamplesBlack_RatherThanSmearingTheEdgeColour()
        {
            // Four pixels running right from x = -2: the first two are off the left edge.
            var fixture = PixelMappingTestData.Strip("Edge", originX: -2f, originY: 100f, pixelCount: 4);
            var map = PixelMappingTestData.MapOf(fixture);
            var layout = PixelMapLayoutBuilder.Build(map, new TestPixelTypes());
            var sampleMap = PixelSampleMapBuilder.Build(map, layout);

            Assert.Equal(PixelSampleMap.Blank, sampleMap.ByteOffsets[0]);
            Assert.Equal(PixelSampleMap.Blank, sampleMap.ByteOffsets[1]);
            Assert.Equal(((100 * PixelMappingTestData.ImageWidth) + 0) * 4, sampleMap.ByteOffsets[2]);
            Assert.Equal(((100 * PixelMappingTestData.ImageWidth) + 1) * 4, sampleMap.ByteOffsets[3]);

            var image = PixelMappingTestData.SyntheticImage();
            var samples = new RgbSample[sampleMap.PixelCount];
            sampleMap.Sample(image, samples);
            Assert.Equal(default, samples[0]);
            Assert.Equal(default, samples[1]);
            Assert.NotEqual(default, samples[2]);
        }

        [Fact]
        public void PixelDraggedOffTheBottomOrRight_AlsoSamplesBlack()
        {
            var wide = PixelMappingTestData.Strip("Right", originX: PixelMappingTestData.ImageWidth - 2, originY: 10f, pixelCount: 4);
            var below = PixelMappingTestData.Strip("Below", originX: 10f, originY: PixelMappingTestData.ImageHeight, pixelCount: 2, startChannel: 4 * 3);

            var map = PixelMappingTestData.MapOf(wide, below);
            var layout = PixelMapLayoutBuilder.Build(map, new TestPixelTypes());
            var offsets = PixelSampleMapBuilder.Build(map, layout).ByteOffsets;

            Assert.NotEqual(PixelSampleMap.Blank, offsets[0]);
            Assert.NotEqual(PixelSampleMap.Blank, offsets[1]);
            Assert.Equal(PixelSampleMap.Blank, offsets[2]);
            Assert.Equal(PixelSampleMap.Blank, offsets[3]);
            Assert.Equal(PixelSampleMap.Blank, offsets[4]);
            Assert.Equal(PixelSampleMap.Blank, offsets[5]);
        }

        [Fact]
        public void Sample_RejectsAnImageSmallerThanTheMapWasBuiltFor()
        {
            var map = PixelMappingTestData.MapOf(PixelMappingTestData.Strip("A", 0, 0));
            var layout = PixelMapLayoutBuilder.Build(map, new TestPixelTypes());
            var sampleMap = PixelSampleMapBuilder.Build(map, layout);

            var tooSmall = new byte[16];
            var destination = new RgbSample[sampleMap.PixelCount];

            Assert.Throws<ArgumentException>(() => sampleMap.Sample(tooSmall, destination));
        }

        [Fact]
        public void BlankedRun_ConsumesItsChannels_BecauseWireAddressingIsPositional()
        {
            var fixture = PixelMappingTestData.Strip("Tail", 0, 0, pixelCount: 10);
            fixture.BlankedPixels.Add(new PixelRange(8, 2));

            var map = PixelMappingTestData.MapOf(fixture);
            var layout = PixelMapLayoutBuilder.Build(map, new TestPixelTypes());

            Assert.Equal(10, layout.TotalPixelCount);
            Assert.Equal(30, layout.TotalChannelCount);
            Assert.Equal(30, layout.Outputs[0].ChannelCount);
        }
    }
}
