using System;
using System.Linq;
using ShowControl.PixelMapping;
using Xunit;

namespace ShowControl.Tests
{
    public class PixelMapTests
    {
        [Fact]
        public void GenerateLine_AtUnitSpacingAndZeroAngle_ProducesExactWholeNumberOffsets()
        {
            var offsets = PixelLayoutTools.GenerateLine(720);

            Assert.Equal(1440, offsets.Length);
            for (int i = 0; i < 720; i++)
            {
                Assert.Equal((float)i, offsets[i * 2]);
                Assert.Equal(0f, offsets[(i * 2) + 1]);
            }
        }

        /// <summary>
        /// MathF.Cos(MathF.PI / 2) is -4.37e-08, not zero, which would leave a vertical strip with
        /// 720 slightly different x offsets instead of one.
        /// </summary>
        [Theory]
        [InlineData(90f, 0f, 1f)]
        [InlineData(180f, -1f, 0f)]
        [InlineData(270f, 0f, -1f)]
        [InlineData(-90f, 0f, -1f)]
        [InlineData(360f, 1f, 0f)]
        public void GenerateLine_OnQuarterTurns_SnapsToExactAxes(float angle, float expectedDx, float expectedDy)
        {
            var offsets = PixelLayoutTools.GenerateLine(3, spacing: 2f, angleDegrees: angle);

            Assert.Equal(expectedDx * 2f, offsets[2]);
            Assert.Equal(expectedDy * 2f, offsets[3]);
            Assert.Equal(expectedDx * 4f, offsets[4]);
            Assert.Equal(expectedDy * 4f, offsets[5]);
        }

        [Fact]
        public void GenerateLine_RejectsAFixtureWithNoPixels()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PixelLayoutTools.GenerateLine(0));
        }

        /// <summary>
        /// Dragging a fixture is the interactive path, and it has to stay cheap: it moves the origin
        /// and nothing else, which is also why a positional edit is a two-number diff in the file.
        /// </summary>
        [Fact]
        public void DraggingAFixture_MovesOnlyTheOrigin_AndShiftsEveryPixelEqually()
        {
            var fixture = PixelMappingTestData.Strip("Drag", originX: 10f, originY: 20f, pixelCount: 5);
            var before = PixelMapGeometry.Resolve(fixture);
            var offsetsBefore = (float[])fixture.PixelOffsets.Clone();

            fixture.OriginX += 7f;
            fixture.OriginY -= 3f;

            var after = PixelMapGeometry.Resolve(fixture);

            Assert.Equal(offsetsBefore, fixture.PixelOffsets);
            for (int i = 0; i < before.Length; i++)
            {
                Assert.Equal(before[i].X + 7f, after[i].X);
                Assert.Equal(before[i].Y - 3f, after[i].Y);
            }
        }

        /// <summary>
        /// The pivot gesture is editor-only and nothing about it is persisted — it rewrites offsets
        /// and the file records the result. The pure operation is pinned here so the UI on top of it
        /// has a tested foundation.
        /// </summary>
        [Fact]
        public void RotateSection_BendsTheRunFromThePivotOnward_AndLeavesTheRunBeforeItAlone()
        {
            var offsets = PixelLayoutTools.GenerateLine(5); // (0,0) (1,0) (2,0) (3,0) (4,0)

            PixelLayoutTools.RotateSection(offsets, fromIndex: 2, turnDegrees: 90f);

            // Pixels 0..2 are untouched; the pivot itself does not move.
            Assert.Equal(new[] { 0f, 0f, 1f, 0f, 2f, 0f }, offsets.Take(6).ToArray());

            // Pixels 3 and 4 swing to run downward from the pivot at (2,0).
            Assert.Equal(2f, offsets[6], 4);
            Assert.Equal(1f, offsets[7], 4);
            Assert.Equal(2f, offsets[8], 4);
            Assert.Equal(2f, offsets[9], 4);
        }

        [Fact]
        public void RotateSection_AtTheLastPixel_ChangesNothing()
        {
            var offsets = PixelLayoutTools.GenerateLine(3);
            var before = (float[])offsets.Clone();

            PixelLayoutTools.RotateSection(offsets, fromIndex: 2, turnDegrees: 45f);

            Assert.Equal(before, offsets);
        }

        [Fact]
        public void RotateSection_RejectsAPivotOutsideTheFixture()
        {
            var offsets = PixelLayoutTools.GenerateLine(3);
            Assert.Throws<ArgumentOutOfRangeException>(() => PixelLayoutTools.RotateSection(offsets, 3, 10f));
        }

        [Fact]
        public void Serializer_RoundTripsEveryField()
        {
            var original = new PixelMap
            {
                Name = "Round trip",
                Fixtures =
                {
                    new FixtureDefinition
                    {
                        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                        Name = "Bent strip",
                        Enabled = false,
                        Output = new OutputTarget
                        {
                            Interface = OutputInterfaceType.ArtNet,
                            IpAddress = "10.0.0.5",
                            Port = 6454,
                            Universe = 7,
                            StartChannel = 120
                        },
                        PixelType = "AsteraAX3",
                        ColorOrder = ColorOrder.Grb,
                        OriginX = 12.5f,
                        OriginY = -3.25f,
                        PixelOffsets = new[] { 0f, 0f, 1.5f, -2.25f, 3f, 4f },
                        BlankedPixels = { new PixelRange(1, 2) }
                    }
                }
            };

            var restored = PixelMapSerializer.Deserialize(PixelMapSerializer.Serialize(original));

            var fixture = Assert.Single(restored.Fixtures);
            var expected = original.Fixtures[0];
            Assert.Equal(expected.Id, fixture.Id);
            Assert.Equal(expected.Name, fixture.Name);
            Assert.False(fixture.Enabled);
            Assert.Equal(OutputInterfaceType.ArtNet, fixture.Output.Interface);
            Assert.Equal("10.0.0.5", fixture.Output.IpAddress);
            Assert.Equal(6454, fixture.Output.Port);
            Assert.Equal(7, fixture.Output.Universe);
            Assert.Equal(120, fixture.Output.StartChannel);
            Assert.Equal("AsteraAX3", fixture.PixelType);
            Assert.Equal(ColorOrder.Grb, fixture.ColorOrder);
            Assert.Equal(12.5f, fixture.OriginX);
            Assert.Equal(-3.25f, fixture.OriginY);
            Assert.Equal(expected.PixelOffsets, fixture.PixelOffsets);
            Assert.Equal(expected.BlankedPixels, fixture.BlankedPixels);
            Assert.Equal(PixelMap.CurrentVersion, restored.Version);
            Assert.Equal("Round trip", restored.Name);
        }

        [Fact]
        public void Serializer_RoundTripsTheShippedDefaultMap_Unchanged()
        {
            var map = DefaultPixelMap.Load();
            var restored = PixelMapSerializer.Deserialize(PixelMapSerializer.Serialize(map));

            Assert.Equal(map.Fixtures.Count, restored.Fixtures.Count);
            for (int i = 0; i < map.Fixtures.Count; i++)
            {
                Assert.Equal(map.Fixtures[i].Id, restored.Fixtures[i].Id);
                Assert.Equal(map.Fixtures[i].Enabled, restored.Fixtures[i].Enabled);
                Assert.Equal(map.Fixtures[i].OriginY, restored.Fixtures[i].OriginY);
                Assert.Equal(map.Fixtures[i].PixelOffsets, restored.Fixtures[i].PixelOffsets);
            }
        }

        [Fact]
        public void ShippedDefaultMap_UsesTheSameOffsetsGenerateLineWouldProduce()
        {
            var expected = PixelLayoutTools.GenerateLine(720);

            foreach (var fixture in DefaultPixelMap.Load().Fixtures)
            {
                Assert.Equal(expected, fixture.PixelOffsets);
                Assert.Equal(720, fixture.PixelCount);
                Assert.Equal(new PixelRange(708, 12), Assert.Single(fixture.BlankedPixels));
            }
        }

        [Fact]
        public void Serializer_RejectsAMapWrittenByANewerBuild()
        {
            var future = PixelMapSerializer.Serialize(new PixelMap { Version = PixelMap.CurrentVersion + 1 });

            var error = Assert.Throws<PixelMapFormatException>(() => PixelMapSerializer.Deserialize(future));
            Assert.Contains("version", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Serializer_RejectsGarbage_SoACorruptFileFallsThroughToTheNextSource()
        {
            Assert.Throws<PixelMapFormatException>(() => PixelMapSerializer.Deserialize("{ not json"));
            Assert.Throws<PixelMapFormatException>(() => PixelMapSerializer.Deserialize(string.Empty));
        }

        [Fact]
        public void PixelCount_IsDerivedFromTheOffsets_SoTheyCannotDisagree()
        {
            var fixture = new FixtureDefinition { PixelOffsets = PixelLayoutTools.GenerateLine(37) };

            Assert.Equal(37, fixture.PixelCount);
        }

        [Fact]
        public void Clone_IsDeep_SoAnEditInProgressCannotMutateTheLiveMap()
        {
            var map = PixelMappingTestData.MapOf(PixelMappingTestData.Strip("A", 1f, 2f));
            var clone = map.Clone();

            clone.Fixtures[0].OriginX = 99f;
            clone.Fixtures[0].PixelOffsets[0] = 99f;
            clone.Fixtures[0].Output.IpAddress = "10.9.9.9";
            clone.Fixtures[0].BlankedPixels.Add(new PixelRange(0, 1));

            Assert.Equal(1f, map.Fixtures[0].OriginX);
            Assert.Equal(0f, map.Fixtures[0].PixelOffsets[0]);
            Assert.Equal("192.168.3.207", map.Fixtures[0].Output.IpAddress);
            Assert.Empty(map.Fixtures[0].BlankedPixels);
        }

        [Theory]
        [InlineData(ColorOrder.Rgb, 10, 20, 30)]
        [InlineData(ColorOrder.Grb, 20, 10, 30)]
        [InlineData(ColorOrder.Bgr, 30, 20, 10)]
        [InlineData(ColorOrder.Rbg, 10, 30, 20)]
        [InlineData(ColorOrder.Gbr, 20, 30, 10)]
        [InlineData(ColorOrder.Brg, 30, 10, 20)]
        public void ColorOrder_PermutesTheFirstThreeChannels(ColorOrder order, byte first, byte second, byte third)
        {
            var channels = new byte[] { 10, 20, 30 };

            order.Apply(channels);

            Assert.Equal(new byte[] { first, second, third }, channels);
        }

        [Fact]
        public void ColorOrder_LeavesChannelsBeyondTheThirdAlone()
        {
            var channels = new byte[] { 10, 20, 30, 40, 50 };

            ColorOrder.Bgr.Apply(channels);

            Assert.Equal(new byte[] { 30, 20, 10, 40, 50 }, channels);
        }
    }
}
