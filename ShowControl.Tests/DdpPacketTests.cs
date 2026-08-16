using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using ShowControl.Devices.Output;
using Xunit;

namespace ShowControl.Tests
{
    public class DdpPacketTests
    {
        [Fact]
        public void BuildPacket_HeaderLayout_MatchesDdpV1()
        {
            var data = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();

            var packet = DdpDevice.BuildPacket(data, sourceOffset: 100, length: 200,
                deviceOffset: 0x01020304, sequence: 7, dataType: 0x01, deviceId: 0x2A);

            Assert.Equal(10 + 200, packet.Length);
            Assert.Equal(0x40, packet[0]); // version 1, PUSH deliberately never set
            Assert.Equal(7, packet[1]);    // sequence
            Assert.Equal(0x01, packet[2]); // data type
            Assert.Equal(0x2A, packet[3]); // device id
            // big-endian 32-bit device offset
            Assert.Equal(0x01, packet[4]);
            Assert.Equal(0x02, packet[5]);
            Assert.Equal(0x03, packet[6]);
            Assert.Equal(0x04, packet[7]);
            // big-endian 16-bit length
            Assert.Equal(200 >> 8, packet[8]);
            Assert.Equal(200 & 0xFF, packet[9]);
            // payload copied from sourceOffset
            Assert.Equal(data.Skip(100).Take(200), packet.Skip(10));
        }

        [Fact]
        public void GetChunks_FullFrame_SplitsInto1440ByteSpans()
        {
            // one 8640-channel WLED controller frame (the production configuration)
            var chunks = DdpDevice.GetChunks(dataLength: 8640, dataOffset: 0).ToArray();

            Assert.Equal(6, chunks.Length);
            Assert.All(chunks, c => Assert.Equal(1440, c.chunkSize));
            Assert.Equal(new[] { 0, 1440, 2880, 4320, 5760, 7200 }, chunks.Select(c => c.sourceOffset));
            Assert.Equal(chunks.Select(c => c.sourceOffset), chunks.Select(c => c.deviceOffset));
        }

        [Fact]
        public void GetChunks_SmallFrame_SinglePacket()
        {
            var chunks = DdpDevice.GetChunks(dataLength: 510, dataOffset: 0).ToArray();

            var chunk = Assert.Single(chunks);
            Assert.Equal((0, 510, 0), chunk);
        }

        [Fact]
        public void GetChunks_PartialTail_LastChunkIsRemainder()
        {
            var chunks = DdpDevice.GetChunks(dataLength: 1500, dataOffset: 100).ToArray();

            Assert.Equal(2, chunks.Length);
            Assert.Equal((0, 1440, 100), chunks[0]);
            Assert.Equal((1440, 60, 1540), chunks[1]);
        }

        [Fact]
        public void GetChunks_ZeroLength_YieldsNothing()
        {
            Assert.Empty(DdpDevice.GetChunks(dataLength: 0, dataOffset: 0));
        }

        [Fact]
        public void Constructor_DoesNotOpenSocket_SoStartupSurvivesAnUnreachableController()
        {
            // 192.0.2.0/24 is TEST-NET-1: guaranteed unroutable. Connecting eagerly in
            // the constructor is what used to take startup down when the venue network
            // was not up yet.
            var device = new DdpDevice(new byte[] { 192, 0, 2, 1 }, 8640);

            Assert.Equal(8640, device.MaxChannels);
            Assert.Equal("DDP 192.0.2.1:4048 (8640ch)", device.ToString());

            device.Dispose();
        }

        [Fact]
        public void Write_ConnectsLazily_AndSucceedsOverLoopback()
        {
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
            using var device = new DdpDevice(new byte[] { 127, 0, 0, 1 }, 30, port: port);

            device.Write(new byte[30]);

            var remote = new IPEndPoint(IPAddress.Any, 0);
            var received = listener.Receive(ref remote);
            Assert.Equal(10 + 30, received.Length);
            Assert.Equal(0x40, received[0]);
        }

        [Fact]
        public void Write_AfterDispose_Throws()
        {
            var device = new DdpDevice(new byte[] { 127, 0, 0, 1 }, 30);
            device.Dispose();

            // Write throws synchronously rather than returning a faulted task, which is
            // what lets the caller attribute the failure to this specific device.
            void WriteToDisposedDevice() => device.Write(new byte[30]);

            Assert.Throws<ObjectDisposedException>(WriteToDisposedDevice);
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var device = new DdpDevice(new byte[] { 127, 0, 0, 1 }, 30);
            device.Dispose();
            device.Dispose();
        }

        [Fact]
        public void NextSequence_Walks1To15_ThenWrapsTo1_Never0()
        {
            byte s = 1;
            var seen = new System.Collections.Generic.List<byte>();
            for (int i = 0; i < 30; i++)
            {
                s = DdpDevice.NextSequence(s);
                seen.Add(s);
            }

            Assert.Equal(Enumerable.Range(0, 30).Select(i => (byte)(((i + 1) % 15) + 1)), seen);
            Assert.DoesNotContain((byte)0, seen);
            Assert.Equal(15, seen.Max());
        }
    }
}
