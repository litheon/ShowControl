using System;
using System.Drawing;
using ShowControl.Devices.DMX;
using Xunit;

namespace ShowControl.Tests
{
    // Tests construct fresh instances instead of the shared mutable .Instance singletons.
    public class DmxFixtureTests
    {
        public static TheoryData<IDmxDevice, int> AllFixtures => new()
        {
            { new WS2812B(), 3 },
            { new AsteraAX3(), 4 },
            { new AsteraTitan(), 4 },
            { new JLPow(), 6 },
            { new BetopperDJ(), 7 },
            { new BetopperDJNew(), 7 },
            { new Missyee(), 7 },
            { new ChauvetFreedom(), 8 },
            { new Sheds(), 10 },
        };

        [Theory]
        [MemberData(nameof(AllFixtures))]
        public void RgbMode_VectorLengthMatchesChannelCount(IDmxDevice fixture, int expectedChannels)
        {
            // RGB is the production mode — every fixture constructor defaults to it.
            fixture.Color = Color.FromArgb(0xFF, 10, 20, 30);

            Assert.Equal(expectedChannels, fixture.ChannelCount);
            Assert.Equal(expectedChannels, fixture.GetDMXVector().Length);
        }

        [Fact]
        public void Sheds_RgbLayout()
        {
            var sheds = new Sheds
            {
                Color = Color.FromArgb(0xAA, 0x11, 0x22, 0x33),
                MasterFader = 0xC8,
            };

            Assert.Equal(
                new byte[] { 0xC8, 0x11, 0x22, 0x33, 0x00, 0x00, 0xAA, 0x00, 0x00, 0x00 },
                sheds.GetDMXVector());
        }

        [Fact]
        public void Sheds_UvLayout_IgnoresColor()
        {
            var sheds = new Sheds
            {
                Mode = DmxDeviceMode.UV,
                Color = Color.FromArgb(0xFF, 0x11, 0x22, 0x33),
                MasterFader = 0x80,
            };

            Assert.Equal(
                new byte[] { 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00 },
                sheds.GetDMXVector());
        }

        [Fact]
        public void Sheds_OffMode_AllZeros()
        {
            var sheds = new Sheds { Mode = DmxDeviceMode.Off, Color = Color.Red, MasterFader = 0xFF };

            Assert.All(sheds.GetDMXVector(), b => Assert.Equal(0, b));
        }

        [Fact]
        public void Sheds_StrobeMode_IsNotImplemented()
        {
            var sheds = new Sheds { Mode = DmxDeviceMode.Strobe };

            Assert.Throws<NotImplementedException>(() => sheds.GetDMXVector());
        }

        [Fact]
        public void WS2812B_FullMasterFader_PreservesPureRed()
        {
            var strip = new WS2812B { Color = Color.FromArgb(0xFF, 255, 0, 0), MasterFader = 0xFF };

            Assert.Equal(new byte[] { 255, 0, 0 }, strip.GetDMXVector());
        }

        [Fact]
        public void WS2812B_ZeroMasterFader_ScalesToBlack()
        {
            var strip = new WS2812B { Color = Color.FromArgb(0xFF, 255, 128, 64), MasterFader = 0x00 };

            Assert.Equal(new byte[] { 0, 0, 0 }, strip.GetDMXVector());
        }

        [Fact]
        public void WS2812B_HalfMasterFader_HalvesBrightness()
        {
            var strip = new WS2812B { Color = Color.FromArgb(0xFF, 255, 0, 0), MasterFader = 128 };

            Assert.Equal(new byte[] { 128, 0, 0 }, strip.GetDMXVector());
        }

        [Fact]
        public void WS2812B_OffMode_AllZeros()
        {
            var strip = new WS2812B { Mode = DmxDeviceMode.Off, Color = Color.Red };

            Assert.Equal(new byte[] { 0, 0, 0 }, strip.GetDMXVector());
        }

        [Fact]
        public void WS2812B_GetDMXVector_MutatesColorProperty()
        {
            // Documents current behavior: the master-fader HSV scaling writes the
            // scaled result back to Color, so repeated calls compound the dimming.
            var strip = new WS2812B { Color = Color.FromArgb(0xFF, 255, 0, 0), MasterFader = 128 };

            var first = strip.GetDMXVector();
            var second = strip.GetDMXVector();

            Assert.Equal(128, first[0]);
            Assert.Equal(64, second[0]);
        }
    }
}
