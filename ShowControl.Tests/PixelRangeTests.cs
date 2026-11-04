using System.Collections.Generic;
using ShowControl.PixelMapping;
using Xunit;

namespace ShowControl.Tests
{
    public class PixelRangeTests
    {
        [Fact]
        public void FormatAndParse_RoundTrip_InTheEditorsNotation()
        {
            var ranges = new List<PixelRange> { new(12, 4), new(40, 1), new(100, 4) };
            string text = PixelRange.Format(ranges);
            Assert.Equal("12-15, 40, 100-103", text);

            Assert.True(PixelRange.TryParseList(text, 200, out var parsed, out string? error));
            Assert.Null(error);
            Assert.Equal(ranges, parsed);
        }

        [Theory]
        [InlineData("", 0)]
        [InlineData("   ", 0)]
        [InlineData("5", 1)]
        [InlineData("5-5", 1)]
        [InlineData("8-5", 4)]               // either way round
        [InlineData("12–15", 4)]             // the en dash the readout prints
        [InlineData("3, 1; 2", 3)]           // separators: comma, semicolon, space
        [InlineData("0-3 4-7", 8)]           // touching ranges merge
        [InlineData("0-5, 3-9", 10)]         // overlapping ones too
        public void Parse_AcceptsTheNotation_AndCanonicalizes(string text, int blankedCount)
        {
            Assert.True(PixelRange.TryParseList(text, 20, out var parsed, out string? error), error);
            int total = 0;
            int lastEnd = -1;
            foreach (var r in parsed)
            {
                Assert.True(r.Start > lastEnd);     // sorted and disjoint, nothing touching
                lastEnd = r.EndExclusive;
                total += r.Count;
            }
            Assert.Equal(blankedCount, total);
        }

        [Theory]
        [InlineData("twelve")]
        [InlineData("1-2-3")]
        [InlineData("-1")]
        [InlineData("20")]
        [InlineData("18-25")]
        public void Parse_RefusesGarbage_AndIndicesOutsideTheFixture(string text)
        {
            Assert.False(PixelRange.TryParseList(text, 20, out _, out string? error));
            Assert.NotNull(error);
            Assert.Contains(text.Split(',')[0].Trim(), error);
        }

        [Fact]
        public void ClipTo_DropsRangesBeyondAShorterRun_AndShortensOneAcrossTheEnd()
        {
            var ranges = new List<PixelRange> { new(2, 2), new(8, 4), new(20, 3) };
            var clipped = PixelRange.ClipTo(ranges, 10);
            Assert.Equal(new List<PixelRange> { new(2, 2), new(8, 2) }, clipped);
        }
    }
}
