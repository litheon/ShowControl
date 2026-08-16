using System;
using System.Linq;
using ShowControl.PixelMapping;
using Xunit;

namespace ShowControl.Tests
{
    public class PixelMapLayoutTests
    {
        /// <summary>
        /// The old channel split was positional, so the single live controller received
        /// targetVector[0..8639] — strips 0-3, image rows 400/410/420/430. The shipped map has to
        /// put exactly those four on 192.168.3.207 at those channel offsets, or the rig looks
        /// different the first time the new build runs.
        /// </summary>
        [Fact]
        public void ShippedDefaultMap_EnablesFourStripsOnOneControllerTotalling8640Channels()
        {
            var map = DefaultPixelMap.Load();
            var layout = PixelMapLayoutBuilder.Build(map, new TestPixelTypes());

            var output = Assert.Single(layout.Outputs);
            Assert.Equal("192.168.3.207", output.Target.IpAddress);
            Assert.Equal(OutputInterfaceType.Ddp, output.Target.Interface);
            Assert.Equal(0, output.ChannelStart);
            Assert.Equal(8640, output.ChannelCount);

            var fixtures = layout.FixtureSlots.ToArray();
            Assert.Equal(4, fixtures.Length);
            Assert.Equal(new[] { 0, 2160, 4320, 6480 }, fixtures.Select(f => f.ChannelStart).ToArray());
            Assert.Equal(new[] { 400f, 410f, 420f, 430f }, fixtures.Select(f => f.Fixture!.OriginY).ToArray());
            Assert.Equal(2880, layout.TotalPixelCount);
            Assert.Equal(8640, layout.TotalChannelCount);
        }

        [Fact]
        public void ShippedDefaultMap_WithEveryStripEnabled_ProducesFourControllersOf8640Channels()
        {
            var layout = PixelMapLayoutBuilder.Build(PixelMappingTestData.AllStripsEnabled(), new TestPixelTypes());

            Assert.Equal(4, layout.Outputs.Count);
            Assert.All(layout.Outputs, o => Assert.Equal(8640, o.ChannelCount));
            Assert.Equal(
                new[] { "192.168.3.207", "192.168.3.208", "192.168.3.93", "192.168.3.103" },
                layout.Outputs.Select(o => o.Target.IpAddress).ToArray());
            Assert.Equal(new[] { 0, 8640, 17280, 25920 }, layout.Outputs.Select(o => o.ChannelStart).ToArray());
            Assert.Equal(11520, layout.TotalPixelCount);
            Assert.Equal(34560, layout.TotalChannelCount);
        }

        [Fact]
        public void DisabledFixtures_AreExcludedEntirely_SoTogglingOneCannotShiftAnotherFixturesData()
        {
            var kept = PixelMappingTestData.Strip("Kept", 0, 0, pixelCount: 4, startChannel: 0);
            var dropped = PixelMappingTestData.Strip("Dropped", 0, 10, pixelCount: 4, ipAddress: "192.168.3.99", enabled: false);
            var after = PixelMappingTestData.Strip("After", 0, 20, pixelCount: 4, startChannel: 12);

            var layout = PixelMapLayoutBuilder.Build(
                PixelMappingTestData.MapOf(kept, dropped, after),
                new TestPixelTypes());

            Assert.Single(layout.Outputs);
            Assert.Equal(8, layout.TotalPixelCount);
            Assert.Equal(24, layout.TotalChannelCount);
            Assert.Equal(new[] { "Kept", "After" }, layout.FixtureSlots.Select(f => f.Fixture!.Name).ToArray());
        }

        [Fact]
        public void FixturesSharingAController_AreGroupedIntoOneOutputOrderedByStartChannel()
        {
            // Declared out of channel order on purpose.
            var second = PixelMappingTestData.Strip("Second", 0, 10, pixelCount: 4, startChannel: 12);
            var first = PixelMappingTestData.Strip("First", 0, 0, pixelCount: 4, startChannel: 0);

            var layout = PixelMapLayoutBuilder.Build(
                PixelMappingTestData.MapOf(second, first),
                new TestPixelTypes());

            Assert.Single(layout.Outputs);
            Assert.Equal(24, layout.Outputs[0].ChannelCount);
            Assert.Equal(new[] { "First", "Second" }, layout.FixtureSlots.Select(f => f.Fixture!.Name).ToArray());
            Assert.Equal(new[] { 0, 12 }, layout.FixtureSlots.Select(f => f.ChannelStart).ToArray());
            Assert.Equal(new[] { 0, 4 }, layout.FixtureSlots.Select(f => f.PixelStart).ToArray());
        }

        [Fact]
        public void OutputDeviceOrder_FollowsTheFirstEnabledFixtureThatTargetsIt()
        {
            var b = PixelMappingTestData.Strip("B", 0, 0, ipAddress: "192.168.3.2");
            var a = PixelMappingTestData.Strip("A", 0, 10, ipAddress: "192.168.3.1");
            var b2 = PixelMappingTestData.Strip("B2", 0, 20, ipAddress: "192.168.3.2", startChannel: 12);

            var layout = PixelMapLayoutBuilder.Build(
                PixelMappingTestData.MapOf(b, a, b2),
                new TestPixelTypes());

            Assert.Equal(
                new[] { "192.168.3.2", "192.168.3.1" },
                layout.Outputs.Select(o => o.Target.IpAddress).ToArray());
        }

        /// <summary>
        /// A hole in a controller's channel space is modelled explicitly, so the sequential surface
        /// walk still lines up instead of silently sliding every later fixture forward.
        /// </summary>
        [Fact]
        public void AGapBetweenFixturesOnOneController_BecomesAGapSlotThatOccupiesChannelsButNoPixels()
        {
            var first = PixelMappingTestData.Strip("First", 0, 0, pixelCount: 4, startChannel: 0);
            var afterGap = PixelMappingTestData.Strip("AfterGap", 0, 10, pixelCount: 4, startChannel: 30);

            var layout = PixelMapLayoutBuilder.Build(
                PixelMappingTestData.MapOf(first, afterGap),
                new TestPixelTypes());

            Assert.Equal(3, layout.Slots.Count);
            var gap = layout.Slots[1];
            Assert.Equal(LayoutSlotKind.Gap, gap.Kind);
            Assert.Equal(12, gap.ChannelStart);
            Assert.Equal(18, gap.ChannelCount);
            Assert.Equal(0, gap.PixelCount);

            Assert.Equal(8, layout.TotalPixelCount);
            Assert.Equal(42, layout.TotalChannelCount);
            Assert.Equal(42, layout.Outputs[0].ChannelCount);
        }

        [Fact]
        public void ALeadingGap_IsEmittedBeforeTheFirstFixture()
        {
            var only = PixelMappingTestData.Strip("Only", 0, 0, pixelCount: 4, startChannel: 9);

            var layout = PixelMapLayoutBuilder.Build(PixelMappingTestData.MapOf(only), new TestPixelTypes());

            Assert.Equal(LayoutSlotKind.Gap, layout.Slots[0].Kind);
            Assert.Equal(0, layout.Slots[0].ChannelStart);
            Assert.Equal(9, layout.Slots[0].ChannelCount);
            Assert.Equal(9, layout.Slots[1].ChannelStart);
            Assert.Equal(21, layout.TotalChannelCount);
        }

        [Fact]
        public void SlotsAreContiguousInBothPixelAndChannelSpace()
        {
            var layout = PixelMapLayoutBuilder.Build(PixelMappingTestData.AllStripsEnabled(), new TestPixelTypes());

            int pixelCursor = 0;
            int channelCursor = 0;
            foreach (var slot in layout.Slots)
            {
                Assert.Equal(pixelCursor, slot.PixelStart);
                Assert.Equal(channelCursor, slot.ChannelStart);
                pixelCursor += slot.PixelCount;
                channelCursor += slot.ChannelCount;
            }
            Assert.Equal(layout.TotalPixelCount, pixelCursor);
            Assert.Equal(layout.TotalChannelCount, channelCursor);
        }

        [Fact]
        public void OverlappingChannelRangesOnOneController_AreRejected()
        {
            var first = PixelMappingTestData.Strip("First", 0, 0, pixelCount: 4, startChannel: 0);
            var overlapping = PixelMappingTestData.Strip("Overlapping", 0, 10, pixelCount: 4, startChannel: 6);

            var map = PixelMappingTestData.MapOf(first, overlapping);
            var pixelTypes = new TestPixelTypes();

            var problems = PixelMapValidator.Validate(map, pixelTypes);
            Assert.Contains(problems, p => p.Contains("overlapping", StringComparison.OrdinalIgnoreCase));
            Assert.Throws<ArgumentException>(() => PixelMapLayoutBuilder.Build(map, pixelTypes));
        }

        [Fact]
        public void SameChannelsOnDifferentControllers_DoNotCount_AsAnOverlap()
        {
            var a = PixelMappingTestData.Strip("A", 0, 0, ipAddress: "192.168.3.1", startChannel: 0);
            var b = PixelMappingTestData.Strip("B", 0, 10, ipAddress: "192.168.3.2", startChannel: 0);

            var problems = PixelMapValidator.Validate(PixelMappingTestData.MapOf(a, b), new TestPixelTypes());

            Assert.Empty(problems);
        }

        [Fact]
        public void AnUnknownPixelType_IsReportedRatherThanGuessedAt()
        {
            var fixture = PixelMappingTestData.Strip("Mystery", 0, 0);
            fixture.PixelType = "NotARealFixture";

            var problems = PixelMapValidator.Validate(PixelMappingTestData.MapOf(fixture), new TestPixelTypes());

            Assert.Contains(problems, p => p.Contains("NotARealFixture", StringComparison.Ordinal));
        }

        [Fact]
        public void BlankedRangeOutsideTheFixture_IsReported()
        {
            var fixture = PixelMappingTestData.Strip("Short", 0, 0, pixelCount: 4);
            fixture.BlankedPixels.Add(new PixelRange(3, 5));

            var problems = PixelMapValidator.Validate(PixelMappingTestData.MapOf(fixture), new TestPixelTypes());

            Assert.Contains(problems, p => p.Contains("blanks pixels", StringComparison.Ordinal));
        }

        [Fact]
        public void ShippedDefaultMap_IsValid()
        {
            Assert.Empty(PixelMapValidator.Validate(DefaultPixelMap.Load(), new TestPixelTypes()));
            Assert.Empty(PixelMapValidator.Validate(PixelMappingTestData.AllStripsEnabled(), new TestPixelTypes()));
        }
    }
}
