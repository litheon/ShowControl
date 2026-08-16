using System.Collections.Generic;
using System.Threading.Tasks;
using ShowControl.Devices.Output;
using Xunit;

namespace ShowControl.Tests
{
    public class ArtNetDeviceTests
    {
        private sealed class FakeArtNetController : IArtNetController
        {
            public readonly List<(byte[] ip, byte universe, byte sourcePort, byte[] data, byte sequence)> Sent = new();
            public int SyncCount;

            public Task SendArtDMX(byte[] ipAddress, byte universe, byte sourcePort, byte[] data, byte sequence)
            {
                Sent.Add((ipAddress, universe, sourcePort, data, sequence));
                return Task.CompletedTask;
            }

            public Task SendSync()
            {
                SyncCount++;
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task Write_PassesThroughIpUniverseAndData()
        {
            var controller = new FakeArtNetController();
            var ip = new byte[] { 192, 168, 3, 207 };
            var device = new ArtNetDevice(controller, universe: 5, ipAddress: ip, maxChannels: 510);
            var frame = new byte[] { 1, 2, 3 };

            await device.Write(frame);

            var sent = Assert.Single(controller.Sent);
            Assert.Same(ip, sent.ip);
            Assert.Equal(5, sent.universe);
            Assert.Equal(0, sent.sourcePort);
            Assert.Same(frame, sent.data);
            Assert.Equal(510, device.MaxChannels);
        }

        [Fact]
        public async Task Write_SequenceStartsAt0_ThenWalks1To254_AndWraps()
        {
            // Pins current behavior: the very first frame goes out with sequence 0,
            // after which the sequence walks 1..254 and wraps back to 1.
            var controller = new FakeArtNetController();
            var device = new ArtNetDevice(controller, 0, new byte[] { 10, 0, 0, 1 }, 510);

            for (int i = 0; i < 300; i++)
                await device.Write(new byte[] { 0 });

            Assert.Equal(0, controller.Sent[0].sequence);
            for (int i = 1; i < 300; i++)
                Assert.Equal((byte)(((i - 1) % 254) + 1), controller.Sent[i].sequence);
        }
    }
}
