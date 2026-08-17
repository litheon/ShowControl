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
        /// <para>
        /// Run against the legacy rig on the legacy 1080x960 image: the loop's constants are that
        /// image's geometry, and the shipped map has since been scaled to a larger render target.
        /// What keeps the rig honest across that move is this test plus
        /// <see cref="ShippedDefaultMap_IsTheLegacyRigScaledToTheCurrentImage"/> — together they say
        /// the sampler is faithful and the shipped map is the same rig.
        /// </para>
        /// </summary>
        [Fact]
        public void LegacySamplingReference_AndLegacyRigWithAllStripsEnabled_ProduceIdenticalPixels()
        {
            var image = PixelMappingTestData.SyntheticImage(
                PixelMappingTestData.LegacyImageWidth,
                PixelMappingTestData.LegacyImageHeight);
            var expected = PixelMappingTestData.LegacySample(
                image,
                PixelMappingTestData.LegacyImageWidth,
                PixelMappingTestData.LegacyImageHeight,
                PixelMappingTestData.LegacyPixelCount);

            var map = PixelMappingTestData.LegacyRigWithAllStripsEnabled();
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
        /// The render target grew; the rig did not move. Every fixture in the shipped map has to be
        /// its legacy self scaled by the same factor as the image — same shape, same fraction of the
        /// picture, same wiring — or the show looks different at the new resolution.
        /// </summary>
        [Fact]
        public void ShippedDefaultMap_IsTheLegacyRigScaledToTheCurrentImage()
        {
            var shipped = DefaultPixelMap.Load();
            var legacy = PixelMappingTestData.LegacyRig();
            float scale = PixelMappingTestData.LegacyScale;

            Assert.Equal(PixelMap.DefaultImageWidth, shipped.ImageWidth);
            Assert.Equal(PixelMap.DefaultImageHeight, shipped.ImageHeight);
            Assert.Equal(legacy.ImageWidth * scale, shipped.ImageWidth);
            Assert.Equal(legacy.ImageHeight * scale, shipped.ImageHeight);
            Assert.Equal(legacy.Fixtures.Count, shipped.Fixtures.Count);

            for (int i = 0; i < legacy.Fixtures.Count; i++)
            {
                var was = legacy.Fixtures[i];
                var now = shipped.Fixtures[i];

                Assert.Equal(was.Name, now.Name);
                Assert.Equal(was.Enabled, now.Enabled);
                Assert.Equal(was.PixelType, now.PixelType);
                Assert.Equal(was.Output.Key, now.Output.Key);
                Assert.Equal(was.Output.StartChannel, now.Output.StartChannel);
                Assert.Equal(was.BlankedPixels, now.BlankedPixels);

                Assert.Equal(was.OriginX * scale, now.OriginX);
                Assert.Equal(was.OriginY * scale, now.OriginY);
                Assert.Equal(was.PixelCount, now.PixelCount);
                for (int offset = 0; offset < was.PixelOffsets.Length; offset++)
                {
                    Assert.Equal(was.PixelOffsets[offset] * scale, now.PixelOffsets[offset]);
                }
            }
        }

        /// <summary>
        /// The scaled rows and columns the rig lands on, spelled out. Scaling by 1.25 puts every
        /// other strip on a half row, which floors onto the row below — sampling positions are
        /// deliberately floored rather than rounded, and this pins that.
        /// </summary>
        [Fact]
        public void DefaultMap_PlacesStripKOnTheScaledImageRow_AndBlanksTheLastTwelvePixels()
        {
            var map = PixelMappingTestData.AllStripsEnabled();
            var layout = PixelMapLayoutBuilder.Build(map, new TestPixelTypes());
            var sampleMap = PixelSampleMapBuilder.Build(map, layout);
            var offsets = sampleMap.ByteOffsets;
            float scale = PixelMappingTestData.LegacyScale;

            for (int strip = 0; strip < PixelMappingTestData.StripCount; strip++)
            {
                int row = (int)MathF.Floor((400f + (10f * strip)) * scale);
                int baseIndex = strip * PixelMappingTestData.PixelsPerStrip;

                for (int column = 0; column < 708; column++)
                {
                    int expected = ((row * PixelMappingTestData.ImageWidth) + (int)MathF.Floor(column * scale)) * 4;
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
