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
        /// The hinge is what the per-pixel editing gesture rides on: the grabbed pixel and everything
        /// after it swing about the pixel before it to point at the target, and the run before the
        /// hinge does not move. Dragging pixel 3 of a horizontal strip to sit below pixel 2 makes a
        /// right angle there — the corner a physical strip actually bends at.
        /// </summary>
        [Fact]
        public void PointSectionAt_SwingsTheRunFromTheHandleOnward_AboutThePixelBeforeIt()
        {
            var offsets = PixelLayoutTools.GenerateLine(6, spacing: 2f);

            float? heading = PixelLayoutTools.PointSectionAt(offsets, handleIndex: 3, targetX: 4f, targetY: 10f);

            Assert.Equal(90f, heading);
            // Pixels 0..2 untouched.
            Assert.Equal(new[] { 0f, 0f, 2f, 0f, 4f, 0f }, offsets.Take(6).ToArray());
            // Pixels 3..5 now run straight down from pixel 2, still two units apart.
            Assert.Equal(4f, offsets[6], 4);
            Assert.Equal(2f, offsets[7], 4);
            Assert.Equal(4f, offsets[8], 4);
            Assert.Equal(4f, offsets[9], 4);
            Assert.Equal(4f, offsets[10], 4);
            Assert.Equal(6f, offsets[11], 4);
        }

        /// <summary>A rigid swing: a corner already further along the run is carried with it.</summary>
        [Fact]
        public void PointSectionAt_KeepsTheShapeOfTheSectionItSwings()
        {
            var offsets = PixelLayoutTools.GenerateLine(6, spacing: 1f);
            PixelLayoutTools.RotateSection(offsets, fromIndex: 4, turnDegrees: 90f); // corner at pixel 4
            float cornerBefore = PixelLayoutTools.MeasureBend(offsets);

            PixelLayoutTools.PointSectionAt(offsets, handleIndex: 2, targetX: 1f, targetY: 5f); // bend at pixel 1

            // Two corners now: the new one and the one that came along.
            Assert.Equal(cornerBefore + 90f, PixelLayoutTools.MeasureBend(offsets), 3);
            for (int i = 1; i < 6; i++)
            {
                Assert.Equal(1f, Distance(offsets, i - 1, i), 3);
            }
        }

        [Fact]
        public void PointSectionAt_OnPixelZero_RotatesTheWholeRunAboutIt()
        {
            var offsets = PixelLayoutTools.GenerateLine(4, spacing: 1f);

            float? heading = PixelLayoutTools.PointSectionAt(offsets, handleIndex: 0, targetX: 0f, targetY: -7f);

            Assert.Equal(270f, heading);
            Assert.Equal(new[] { 0f, 0f, 0f, -1f, 0f, -2f, 0f, -3f }, offsets.Select(v => MathF.Round(v, 4)).ToArray());
        }

        /// <summary>
        /// A right angle has to be hittable by eye, so a heading near a multiple of 45° snaps to it
        /// — and one clearly off it does not, so a deliberate shallow angle survives.
        /// </summary>
        [Theory]
        [InlineData(10f, 0.5f, 90f)]    // ~87°: snaps to vertical
        [InlineData(10f, 3f, 73.3f)]    // ~73°: left alone
        public void PointSectionAt_SnapsOntoTheNearestEighthTurn_OnlyWhenClose(float targetY, float targetX, float expected)
        {
            var offsets = PixelLayoutTools.GenerateLine(3, spacing: 1f);

            float? heading = PixelLayoutTools.PointSectionAt(offsets, handleIndex: 1, targetX, targetY, snapToleranceDegrees: 4f);

            Assert.Equal(expected, heading!.Value, 1);
        }

        /// <summary>
        /// Every bend starts with the cursor on the handle, a fraction of a pixel from the hinge,
        /// where the angle is noise. Nothing moves until the cursor is clear of it.
        /// </summary>
        [Fact]
        public void PointSectionAt_IgnoresATargetTooCloseToTheHinge()
        {
            var offsets = PixelLayoutTools.GenerateLine(4, spacing: 2f);
            var before = (float[])offsets.Clone();

            Assert.Null(PixelLayoutTools.PointSectionAt(offsets, handleIndex: 2, targetX: 2.2f, targetY: 0.6f));
            Assert.Equal(before, offsets);
        }

        [Fact]
        public void PointSectionAt_IsAPureFunctionOfTheRestShapeAndTheTarget()
        {
            var restA = PixelLayoutTools.GenerateLine(12, spacing: 1.5f);
            var restB = (float[])restA.Clone();

            PixelLayoutTools.PointSectionAt(restA, handleIndex: 7, targetX: 3f, targetY: 9f);
            PixelLayoutTools.PointSectionAt(restB, handleIndex: 7, targetX: 3f, targetY: 9f);

            Assert.Equal(restA, restB);
        }

        [Fact]
        public void PointSectionAt_RejectsAHandleOutsideTheFixture()
        {
            var offsets = PixelLayoutTools.GenerateLine(3);
            Assert.Throws<ArgumentOutOfRangeException>(() => PixelLayoutTools.PointSectionAt(offsets, 3, 1f, 1f));
        }

        [Fact]
        public void MeasureBend_IsZeroForAStraightRun_AndTheCornerAngleForABentOne()
        {
            var straight = PixelLayoutTools.GenerateLine(10, spacing: 1.25f, angleDegrees: 30f);
            Assert.Equal(0f, PixelLayoutTools.MeasureBend(straight), 3);

            var bent = PixelLayoutTools.GenerateLine(10);
            PixelLayoutTools.RotateSection(bent, fromIndex: 4, turnDegrees: -60f);
            Assert.Equal(-60f, PixelLayoutTools.MeasureBend(bent), 3);
        }

        /// <summary>
        /// The bend field round-trips: lay a run out at N degrees and it measures N, so what the
        /// box shows after Apply is what was typed. A full turn is counted as 360, not folded.
        /// </summary>
        [Fact]
        public void SetPixelCount_CutsTheRun_OrCarriesItOnAlongItsLastLink()
        {
            // A run heading up the image at spacing 2, bent so its last link points right.
            var offsets = PixelLayoutTools.GenerateLine(4, spacing: 2f, angleDegrees: 90f);
            offsets[6] = 2f; offsets[7] = 4f;          // pixel 3 steps right from pixel 2 (0,4)

            var longer = PixelLayoutTools.SetPixelCount(offsets, 6);
            Assert.Equal(12, longer.Length);
            Assert.Equal(offsets, longer[..8]);        // the existing pixels stay put
            Assert.Equal(new[] { 4f, 4f, 6f, 4f }, longer[8..]);   // the rest continue the last link

            var shorter = PixelLayoutTools.SetPixelCount(offsets, 2);
            Assert.Equal(offsets[..4], shorter);

            // One pixel has no link to continue: the fallback lays the new ones out.
            var single = PixelLayoutTools.SetPixelCount(new float[] { 0f, 0f }, 3, fallbackSpacing: 1.5f, fallbackAngleDegrees: 0f);
            Assert.Equal(new[] { 0f, 0f, 1.5f, 0f, 3f, 0f }, single);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(90f)]
        [InlineData(-45f)]
        [InlineData(360f)]
        public void SetBend_LaysOutAnEvenArc_ThatMeasuresBackToTheSameTurn(float degrees)
        {
            var offsets = PixelLayoutTools.GenerateLine(40, spacing: 2f);

            PixelLayoutTools.SetBend(offsets, degrees);

            Assert.Equal(degrees, PixelLayoutTools.MeasureBend(offsets), 2);
            Assert.Equal(2f, PixelLayoutTools.MeasureSpacing(offsets), 3);
            for (int i = 1; i < 40; i++)
            {
                Assert.Equal(2f, Distance(offsets, i - 1, i), 3);
            }
            Assert.Equal(0f, offsets[0]);
            Assert.Equal(0f, offsets[1]);
        }

        [Fact]
        public void SetBend_KeepsTheFirstLinksHeading()
        {
            var offsets = PixelLayoutTools.GenerateLine(8, spacing: 1f, angleDegrees: 90f);

            PixelLayoutTools.SetBend(offsets, 45f);

            Assert.Equal(0f, offsets[2], 4); // first link still straight down
            Assert.Equal(1f, offsets[3], 4);
        }

        /// <summary>
        /// The way back from any shape: straight, evenly spaced, pointing from the first pixel to
        /// where the last was, and pixel 0 where it was so the fixture does not move.
        /// </summary>
        [Fact]
        public void Straighten_UndoesAnyBend_AlongTheChordAtTheMeanSpacing()
        {
            var offsets = PixelLayoutTools.GenerateLine(9, spacing: 1.25f);
            PixelLayoutTools.RotateSection(offsets, fromIndex: 3, turnDegrees: 90f);
            PixelLayoutTools.RotateSection(offsets, fromIndex: 6, turnDegrees: -30f);
            float lastX = offsets[16];
            float lastY = offsets[17];

            PixelLayoutTools.Straighten(offsets);

            Assert.Equal(0f, PixelLayoutTools.MeasureBend(offsets), 3);
            Assert.Equal(1.25f, PixelLayoutTools.MeasureSpacing(offsets), 3);
            Assert.Equal(0f, offsets[0]);
            Assert.Equal(0f, offsets[1]);
            // Along the chord: the last pixel is in the same direction it was, just at the full length.
            Assert.Equal(MathF.Atan2(lastY, lastX), MathF.Atan2(offsets[17], offsets[16]), 3);
        }

        /// <summary>
        /// A U whose last pixel sits straight below its first has a vertical chord, and straightening
        /// along it is bit-exact — the same exactness GenerateLine promises on the quarter turns.
        /// </summary>
        [Fact]
        public void Straighten_AlongAnAxisAlignedChord_IsExact()
        {
            var offsets = PixelLayoutTools.GenerateLine(7);
            PixelLayoutTools.RotateSection(offsets, fromIndex: 2, turnDegrees: 90f);
            PixelLayoutTools.RotateSection(offsets, fromIndex: 4, turnDegrees: 90f);
            Assert.Equal(0f, offsets[12]);
            Assert.Equal(2f, offsets[13]);

            PixelLayoutTools.Straighten(offsets);

            Assert.Equal(new[] { 0f, 0f, 0f, 1f, 0f, 2f, 0f, 3f, 0f, 4f, 0f, 5f, 0f, 6f }, offsets);
        }

        /// <summary>
        /// If anything carries pixel 0 off the origin, rebasing restores the convention
        /// that the origin marks pixel 0 — without moving a single resolved position.
        /// </summary>
        [Fact]
        public void Rebase_PutsPixelZeroBackOnTheOrigin_WithoutMovingAnyPixel()
        {
            var fixture = PixelMappingTestData.Strip("Rebase", originX: 50f, originY: 60f, pixelCount: 4);
            fixture.PixelOffsets[0] -= 3f; // pixel 0 carried off the origin, however it got there
            fixture.PixelOffsets[1] += 2f;
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
