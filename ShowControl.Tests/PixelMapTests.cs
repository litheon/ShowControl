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

        /// <summary>
        /// Flipping is for a strip wired from the other end: the same pixels in the same places,
        /// addressed in the opposite order.
        /// </summary>
        [Fact]
        public void ReverseDirection_TurnsTheRunAround_WithoutMovingWhereItsPixelsLand()
        {
            var fixture = PixelMappingTestData.Strip("Flip", originX: 100f, originY: 50f, pixelCount: 4);
            var before = PixelMapGeometry.Resolve(fixture);

            var shift = PixelLayoutTools.ReverseDirection(fixture.PixelOffsets);
            fixture.OriginX += shift.X;
            fixture.OriginY += shift.Y;

            var after = PixelMapGeometry.Resolve(fixture);
            for (int i = 0; i < before.Length; i++)
            {
                Assert.Equal(before[before.Length - 1 - i], after[i]);
            }
        }

        [Fact]
        public void ReverseDirection_RebasesOntoTheFirstPixel_SoTheOriginStillMarksIt()
        {
            var fixture = PixelMappingTestData.Strip("Flip", originX: 10f, originY: 20f, pixelCount: 5);

            var shift = PixelLayoutTools.ReverseDirection(fixture.PixelOffsets);
            fixture.OriginX += shift.X;
            fixture.OriginY += shift.Y;

            Assert.Equal(new[] { 0f, 0f }, fixture.PixelOffsets.Take(2).ToArray());
            Assert.Equal(new System.Drawing.PointF(14f, 20f), PixelMapGeometry.Resolve(fixture, 0));
            Assert.Equal(new System.Drawing.PointF(10f, 20f), PixelMapGeometry.Resolve(fixture, 4));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(720)]
        public void ReverseDirection_AppliedTwice_RestoresTheFixtureExactly(int pixelCount)
        {
            var fixture = PixelMappingTestData.Strip("There and back", originX: 7f, originY: 3f, pixelCount: pixelCount);
            PixelLayoutTools.RotateSection(fixture.PixelOffsets, fromIndex: pixelCount / 2, turnDegrees: 90f);
            var offsetsBefore = (float[])fixture.PixelOffsets.Clone();

            for (int i = 0; i < 2; i++)
            {
                var shift = PixelLayoutTools.ReverseDirection(fixture.PixelOffsets);
                fixture.OriginX += shift.X;
                fixture.OriginY += shift.Y;
            }

            Assert.Equal(offsetsBefore, fixture.PixelOffsets);
            Assert.Equal(7f, fixture.OriginX);
            Assert.Equal(3f, fixture.OriginY);
        }

        /// <summary>
        /// The rope drag is what the per-pixel editing gesture rides on: the dragged pixel takes
        /// the delta, neighbours are pulled after it only when their link goes taut, and no link is
        /// ever longer than its rest length — the spacing of a physical strip is not negotiable.
        /// </summary>
        [Fact]
        public void DragRope_MovesTheDraggedPixelByTheDelta_AndNeverStretchesALink()
        {
            var offsets = PixelLayoutTools.GenerateLine(10, spacing: 2f);
            PixelLayoutTools.RotateSection(offsets, fromIndex: 5, turnDegrees: 90f); // a bent run, not just a line
            var rest = (float[])offsets.Clone();

            PixelLayoutTools.DragRope(offsets, pixelIndex: 4, deltaX: 3f, deltaY: -5f);

            Assert.Equal(rest[8] + 3f, offsets[8], 3);
            Assert.Equal(rest[9] - 5f, offsets[9], 3);
            for (int i = 1; i < 10; i++)
            {
                float restLength = Distance(rest, i - 1, i);
                Assert.InRange(Distance(offsets, i - 1, i), 0f, restLength + 0.001f);
            }

            // A pull this hard draws both neighbouring links taut, and taut means exactly rest
            // length — the rope neither stretches nor bunches where it is being pulled.
            Assert.Equal(Distance(rest, 3, 4), Distance(offsets, 3, 4), 3);
            Assert.Equal(Distance(rest, 4, 5), Distance(offsets, 4, 5), 3);
        }

        /// <summary>
        /// Pulling an end pixel directly away from the run leaves no slack anywhere, so the whole
        /// rope comes along rigidly — the rope being a rope, and the degenerate case that makes the
        /// model easy to verify.
        /// </summary>
        [Fact]
        public void DragRope_PullingTheEndAwayFromTheRun_DragsTheWholeRopeTaut()
        {
            var offsets = PixelLayoutTools.GenerateLine(5); // along +x; pixel 0 at the origin end

            PixelLayoutTools.DragRope(offsets, pixelIndex: 0, deltaX: -3f, deltaY: 0f);

            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(i - 3f, offsets[i * 2], 3);
                Assert.Equal(0f, offsets[(i * 2) + 1], 3);
            }
        }

        /// <summary>
        /// The slack rule, and what keeps ordinary sculpting local: a link only pulls, never
        /// pushes, so moving a pixel toward its neighbours moves nothing else at all.
        /// </summary>
        [Fact]
        public void DragRope_PushingTowardTheRun_MovesOnlyTheGrabbedPixel()
        {
            var offsets = PixelLayoutTools.GenerateLine(10);
            var rest = (float[])offsets.Clone();

            PixelLayoutTools.DragRope(offsets, pixelIndex: 0, deltaX: 0.5f, deltaY: 0f);

            Assert.Equal(0.5f, offsets[0]);
            Assert.Equal(0f, offsets[1]);
            Assert.Equal(rest.Skip(2), offsets.Skip(2));
        }

        /// <summary>
        /// Dragging along the run is asymmetric on purpose: the arm behind the motion is pulled
        /// taut and comes along; the arm ahead gains slack and stays exactly where it was.
        /// </summary>
        [Fact]
        public void DragRope_DraggingAlongTheRun_PullsOnlyTheArmBehindTheMotion()
        {
            var offsets = PixelLayoutTools.GenerateLine(10);
            var rest = (float[])offsets.Clone();

            PixelLayoutTools.DragRope(offsets, pixelIndex: 5, deltaX: 2f, deltaY: 0f);

            for (int i = 0; i <= 5; i++)
            {
                Assert.Equal(rest[i * 2] + 2f, offsets[i * 2], 3); // behind: dragged along, taut
                Assert.Equal(0f, offsets[(i * 2) + 1], 3);
            }
            for (int i = 6; i < 10; i++)
            {
                Assert.Equal(rest[i * 2], offsets[i * 2]); // ahead: slack, untouched
                Assert.Equal(rest[(i * 2) + 1], offsets[(i * 2) + 1]);
            }
        }

        /// <summary>
        /// Recomputed from rest on every pointer move rather than accumulated, so the result must be
        /// a pure function of (rest shape, delta) — that is what anchors the gesture.
        /// </summary>
        [Fact]
        public void DragRope_IsAPureFunctionOfTheRestShapeAndTheDelta()
        {
            var restA = PixelLayoutTools.GenerateLine(20, spacing: 1.5f);
            var restB = (float[])restA.Clone();

            PixelLayoutTools.DragRope(restA, pixelIndex: 7, deltaX: 4f, deltaY: 6f);
            PixelLayoutTools.DragRope(restB, pixelIndex: 7, deltaX: 4f, deltaY: 6f);

            Assert.Equal(restA, restB);
        }

        [Fact]
        public void DragRope_RejectsAPixelOutsideTheFixture()
        {
            var offsets = PixelLayoutTools.GenerateLine(3);
            Assert.Throws<ArgumentOutOfRangeException>(() => PixelLayoutTools.DragRope(offsets, 3, 1f, 1f));
        }

        /// <summary>
        /// After a rope drag has carried pixel 0 off the origin, rebasing restores the convention
        /// that the origin marks pixel 0 — without moving a single resolved position.
        /// </summary>
        [Fact]
        public void Rebase_PutsPixelZeroBackOnTheOrigin_WithoutMovingAnyPixel()
        {
            var fixture = PixelMappingTestData.Strip("Rebase", originX: 50f, originY: 60f, pixelCount: 4);
            PixelLayoutTools.DragRope(fixture.PixelOffsets, pixelIndex: 0, deltaX: -3f, deltaY: 2f);
            var before = PixelMapGeometry.Resolve(fixture);

            var shift = PixelLayoutTools.Rebase(fixture.PixelOffsets);
            fixture.OriginX += shift.X;
            fixture.OriginY += shift.Y;

            Assert.Equal(new[] { 0f, 0f }, fixture.PixelOffsets.Take(2).ToArray());
            var after = PixelMapGeometry.Resolve(fixture);
            for (int i = 0; i < before.Length; i++)
            {
                Assert.Equal(before[i].X, after[i].X, 3);
                Assert.Equal(before[i].Y, after[i].Y, 3);
            }
        }

        [Fact]
        public void Rebase_WhenPixelZeroIsAlreadyHome_ReturnsZeroAndChangesNothing()
        {
            var offsets = PixelLayoutTools.GenerateLine(4);
            var before = (float[])offsets.Clone();

            Assert.Equal(default, PixelLayoutTools.Rebase(offsets));
            Assert.Equal(before, offsets);
        }

        private static float Distance(float[] offsets, int fromPixel, int toPixel)
        {
            float dx = offsets[toPixel * 2] - offsets[fromPixel * 2];
            float dy = offsets[(toPixel * 2) + 1] - offsets[(fromPixel * 2) + 1];
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        [Fact]
        public void ReverseDirection_OfASinglePixel_ChangesNothing()
        {
            var offsets = PixelLayoutTools.GenerateLine(1);

            Assert.Equal(new System.Drawing.PointF(0f, 0f), PixelLayoutTools.ReverseDirection(offsets));
            Assert.Equal(new[] { 0f, 0f }, offsets);
        }

        /// <summary>
        /// A blanked run is a set of indices into the wire, and flipping re-ends the wire rather
        /// than rewiring it: the dead pixels keep their positions in the strip, which puts them at
        /// the other end of the run in image space.
        /// </summary>
        [Fact]
        public void ReverseDirection_LeavesBlankedPixelsOnTheSameWirePositions()
        {
            var fixture = PixelMappingTestData.Strip("Dead tail", originX: 0f, originY: 0f, pixelCount: 4);
            fixture.BlankedPixels.Add(new PixelRange(2, 2));

            var map = PixelMappingTestData.MapOf(fixture);
            var before = PixelSampleMapBuilder.Build(map, PixelMapLayoutBuilder.Build(map, new TestPixelTypes()));
            Assert.Equal(new[] { 0, 4, PixelSampleMap.Blank, PixelSampleMap.Blank }, before.ByteOffsets.ToArray());

            var shift = PixelLayoutTools.ReverseDirection(fixture.PixelOffsets);
            fixture.OriginX += shift.X;
            fixture.OriginY += shift.Y;

            var after = PixelSampleMapBuilder.Build(map, PixelMapLayoutBuilder.Build(map, new TestPixelTypes()));

            // Same two indices blanked; the live pair now samples the far end of the run instead.
            Assert.Equal(new[] { 12, 8, PixelSampleMap.Blank, PixelSampleMap.Blank }, after.ByteOffsets.ToArray());
            Assert.Equal(new PixelRange(2, 2), Assert.Single(fixture.BlankedPixels));
        }

        /// <summary>
        /// Spacing is not stored, so the editor reads it back off the positions. It has to survive
        /// the round trip the UI makes of it: show the measurement, take a new one, apply it.
        /// </summary>
        [Theory]
        [InlineData(1f)]
        [InlineData(1.25f)]
        [InlineData(7.5f)]
        public void MeasureSpacing_ReadsBackTheSpacingGenerateLineWasGiven(float spacing)
        {
            var offsets = PixelLayoutTools.GenerateLine(720, spacing);

            Assert.Equal(spacing, PixelLayoutTools.MeasureSpacing(offsets), 3);
        }

        [Fact]
        public void MeasureSpacing_OfASinglePixel_IsZero_BecauseThereIsNoGapToMeasure()
        {
            Assert.Equal(0f, PixelLayoutTools.MeasureSpacing(PixelLayoutTools.GenerateLine(1)));
        }

        [Fact]
        public void MeasureSpacing_OfAnAngledRun_IsTheDistanceBetweenPixels_NotTheirXStep()
        {
            var offsets = PixelLayoutTools.GenerateLine(4, spacing: 2f, angleDegrees: 45f);

            Assert.Equal(2f, PixelLayoutTools.MeasureSpacing(offsets), 4);
        }

        [Fact]
        public void SetSpacing_RespacesTheRun_AndLeavesTheFirstPixelWhereItWas()
        {
            var offsets = PixelLayoutTools.GenerateLine(5, spacing: 1.25f);

            PixelLayoutTools.SetSpacing(offsets, 2f);

            Assert.Equal(PixelLayoutTools.GenerateLine(5, spacing: 2f), offsets);
            Assert.Equal(0f, offsets[0]);
            Assert.Equal(0f, offsets[1]);
        }

        /// <summary>
        /// Respacing scales about the first pixel, so a bent fixture keeps its shape — the whole
        /// point of scaling rather than regenerating a straight line at the new spacing.
        /// </summary>
        [Fact]
        public void SetSpacing_OfABentRun_KeepsTheBend()
        {
            var offsets = PixelLayoutTools.GenerateLine(5);
            PixelLayoutTools.RotateSection(offsets, fromIndex: 2, turnDegrees: 90f);
            float before = PixelLayoutTools.MeasureSpacing(offsets);

            PixelLayoutTools.SetSpacing(offsets, before * 3f);

            // Every pixel three times further from the first, corner and all.
            Assert.Equal(new[] { 0f, 0f, 3f, 0f, 6f, 0f, 6f, 3f, 6f, 6f }, offsets);
            Assert.Equal(before * 3f, PixelLayoutTools.MeasureSpacing(offsets), 4);
        }

        [Fact]
        public void SetSpacing_OfASinglePixel_ChangesNothing_RatherThanInventingADirection()
        {
            var offsets = PixelLayoutTools.GenerateLine(1);

            PixelLayoutTools.SetSpacing(offsets, 5f);

            Assert.Equal(new[] { 0f, 0f }, offsets);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        [InlineData(float.NaN)]
        public void SetSpacing_RejectsASpacingThatIsNotAPositiveNumber(float spacing)
        {
            var offsets = PixelLayoutTools.GenerateLine(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => PixelLayoutTools.SetSpacing(offsets, spacing));
        }

        [Fact]
        public void Scale_MovesEveryPixelRelativeToTheFirst_LeavingTheFixturesOriginPut()
        {
            var fixture = PixelMappingTestData.Strip("Scaled", originX: 100f, originY: 50f, pixelCount: 3);

            PixelLayoutTools.Scale(fixture.PixelOffsets, 4f);

            Assert.Equal(new[] { 0f, 0f, 4f, 0f, 8f, 0f }, fixture.PixelOffsets);
            Assert.Equal(new System.Drawing.PointF(100f, 50f), PixelMapGeometry.Resolve(fixture, 0));
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

        /// <summary>
        /// Spacing is 1.25 image units, not 1: the strips were scaled with the render target so they
        /// still span the same fraction of the picture.
        /// </summary>
        [Fact]
        public void ShippedDefaultMap_UsesTheSameOffsetsGenerateLineWouldProduce()
        {
            var expected = PixelLayoutTools.GenerateLine(720, PixelMappingTestData.LegacyScale);

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
