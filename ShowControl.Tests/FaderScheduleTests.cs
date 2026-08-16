using System;
using ShowControl.Utilities;
using Xunit;

namespace ShowControl.Tests
{
    public class FaderScheduleTests
    {
        // The real APOG dim curve (currently commented out in RenderService in favor
        // of an all-255 schedule) — used here as representative data.
        private static readonly (double hour, int value)[] DimCurve = new[]
        {
            (20.0,   255),  // 8:00 PM — start dimming
            (21.0,   255),  // 9:00 PM
            (21.75,  248),  // 9:45 PM
            (22.75,  175),  // 10:45 PM — dimmest
            (27.5,   175),  // 3:30 AM — start undimming
            (28.333, 248),  // 4:20 AM
            (29.15,  255),  // 5:09 AM
            (29.5,   255),  // 5:30 AM — back to full
        };

        [Theory]
        [InlineData(20.0, 255)]
        [InlineData(21.75, 248)]
        [InlineData(22.75, 175)]
        [InlineData(27.5, 175)]
        [InlineData(29.5, 255)]
        public void Evaluate_AtWaypoint_ReturnsWaypointValue(double hour, int expected)
        {
            Assert.Equal(expected, FaderSchedule.Evaluate(DimCurve, hour));
        }

        [Fact]
        public void Evaluate_BetweenWaypoints_Interpolates()
        {
            // midpoint of the 21.75→22.75 span: lerp 248→175 = 211.5, Math.Round → 212
            Assert.Equal(212, FaderSchedule.Evaluate(DimCurve, 22.25));
            // flat span stays flat
            Assert.Equal(175, FaderSchedule.Evaluate(DimCurve, 25.0));
        }

        [Fact]
        public void Evaluate_BeforeFirstWaypoint_ClampsToFirst()
        {
            Assert.Equal(255, FaderSchedule.Evaluate(DimCurve, 12.0));
            Assert.Equal(255, FaderSchedule.Evaluate(DimCurve, 19.99));
        }

        [Fact]
        public void Evaluate_AfterLastWaypoint_ClampsToLast()
        {
            Assert.Equal(255, FaderSchedule.Evaluate(DimCurve, 30.0));
            Assert.Equal(255, FaderSchedule.Evaluate(DimCurve, 35.99));
        }

        [Fact]
        public void Evaluate_SingleWaypoint_AlwaysReturnsItsValue()
        {
            var single = new[] { (22.0, 100) };
            Assert.Equal(100, FaderSchedule.Evaluate(single, 12.0));
            Assert.Equal(100, FaderSchedule.Evaluate(single, 22.0));
            Assert.Equal(100, FaderSchedule.Evaluate(single, 30.0));
        }

        [Fact]
        public void Evaluate_TwoWaypoints_LerpsAcrossWholeSpan()
        {
            var two = new[] { (20.0, 0), (30.0, 100) };
            Assert.Equal(50, FaderSchedule.Evaluate(two, 25.0));
            Assert.Equal(25, FaderSchedule.Evaluate(two, 22.5));
        }

        [Fact]
        public void Evaluate_EmptySchedule_Throws()
        {
            Assert.Throws<ArgumentException>(() => FaderSchedule.Evaluate(Array.Empty<(double, int)>(), 20.0));
        }

        [Theory]
        [InlineData(20, 0, 0, 20.0)]      // 8:00 PM
        [InlineData(15, 30, 0, 15.5)]     // 3:30 PM
        [InlineData(3, 30, 0, 27.5)]      // 3:30 AM → next-day representation
        [InlineData(0, 0, 0, 24.0)]       // midnight
        [InlineData(12, 0, 0, 12.0)]      // noon — earliest representable hour
        [InlineData(11, 59, 0, 35.983333333333334)] // 11:59 AM maps to end of the noon-to-noon day
        public void ToScheduleHour_MapsNoonToNoon(int h, int m, int s, double expected)
        {
            var time = new DateTime(2026, 7, 9, h, m, s);
            Assert.Equal(expected, FaderSchedule.ToScheduleHour(time), 10);
        }
    }
}
