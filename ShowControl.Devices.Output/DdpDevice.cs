using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ShowControl.Devices.Output
{
    /// <summary>
    /// Distributed Display Protocol (DDP) output device.
    /// DDP is a simple protocol for sending LED/pixel data over UDP.
    /// Default port is 4048.
    /// </summary>
    public class DdpDevice : IDmxOutputDevice, IDisposable
    {
        // DDP Header constants
        private const int DDP_PORT = 4048;
        private const int DDP_HEADER_SIZE = 10;
        private const int DDP_MAX_DATA_PER_PACKET = 1440; // Keep under MTU, leaving room for header

        // Flags1 byte bit positions
        private const byte DDP_FLAGS1_VER1 = 0x40;      // Version 1 (bits 7-6 = 01)
        private const byte DDP_FLAGS1_PUSH = 0x01;      // Push/last packet flag
        private const byte DDP_FLAGS1_TIMECODE = 0x10;  // Timecode field present
        private const byte DDP_FLAGS1_STORAGE = 0x08;   // Storage flag
        private const byte DDP_FLAGS1_REPLY = 0x04;     // Reply flag
        private const byte DDP_FLAGS1_QUERY = 0x02;     // Query flag

        // Data type constants
        private const byte DDP_TYPE_UNDEFINED = 0x00;
        private const byte DDP_TYPE_RGB = 0x01;
        private const byte DDP_TYPE_HSL = 0x02;
        private const byte DDP_TYPE_RGBW = 0x03;
        private const byte DDP_TYPE_GRAYSCALE = 0x04;

        private readonly UdpClient _udpClient;
        private readonly byte _deviceId;
        private readonly int _dataOffset;
        private readonly byte _dataType;
        private byte _sequence;
        private bool _disposed;

        public int MaxChannels { get; }

        /// <summary>
        /// Creates a new DDP output device.
        /// </summary>
        /// <param name="ipAddress">IP address of the DDP controller as a byte array (4 bytes for IPv4)</param>
        /// <param name="maxChannels">Maximum number of channels/bytes to send</param>
        /// <param name="deviceId">DDP device ID (0-255), defaults to 0 (broadcast/all)</param>
        /// <param name="dataOffset">Starting offset for the data in the DDP device's buffer</param>
        /// <param name="port">UDP port, defaults to 4048</param>
        /// <param name="dataType">Data type flag (RGB, HSL, etc.), defaults to undefined (0)</param>
        public DdpDevice(
            byte[] ipAddress,
            int maxChannels,
            byte deviceId = 0,
            int dataOffset = 0,
            int port = DDP_PORT,
            byte dataType = DDP_TYPE_UNDEFINED)
        {
            if (ipAddress == null || ipAddress.Length != 4)
                throw new ArgumentException("IP address must be a 4-byte array", nameof(ipAddress));
            if (maxChannels <= 0)
                throw new ArgumentException("Max channels must be positive", nameof(maxChannels));

            MaxChannels = maxChannels;
            _deviceId = deviceId;
            _dataOffset = dataOffset;
            _dataType = dataType;
            _sequence = 1;

            _udpClient = new UdpClient();
            _udpClient.Connect(new IPEndPoint(new IPAddress(ipAddress), port));
        }

        /// <summary>
        /// Creates a new DDP output device using a string IP address.
        /// </summary>
        public DdpDevice(
            string ipAddress,
            int maxChannels,
            byte deviceId = 0,
            int dataOffset = 0,
            int port = DDP_PORT,
            byte dataType = DDP_TYPE_UNDEFINED)
            : this(IPAddress.Parse(ipAddress).GetAddressBytes(), maxChannels, deviceId, dataOffset, port, dataType)
        {
        }

        public Task Write(byte[] dmxData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DdpDevice));

            int dataLength = Math.Min(dmxData.Length, MaxChannels);
            int offset = _dataOffset;
            int remaining = dataLength;
            int sourceOffset = 0;

            while (remaining > 0)
            {
                int chunkSize = Math.Min(remaining, DDP_MAX_DATA_PER_PACKET);
                bool isLastPacket = (remaining - chunkSize) <= 0;

                byte[] packet = BuildPacket(dmxData, sourceOffset, chunkSize, offset, isLastPacket);
                _udpClient.Send(packet, packet.Length);

                offset += chunkSize;
                sourceOffset += chunkSize;
                remaining -= chunkSize;
            }

            // Increment sequence for next frame (1-15, wrap to 1)
            _sequence = (byte)((_sequence % 15) + 1);
            return Task.CompletedTask;
        }

        private byte[] BuildPacket(byte[] data, int sourceOffset, int length, int deviceOffset, bool push)
        {
            byte[] packet = new byte[DDP_HEADER_SIZE + length];

            // Byte 0: Flags1 - Version 1, never push
            // Let WLED's internal render loop handle display timing
            // This avoids partial frame renders when sending multiple packets
            packet[0] = DDP_FLAGS1_VER1;

            // Byte 1: Sequence number (1-15)
            packet[1] = _sequence;

            // Byte 2: Data type
            packet[2] = _dataType;

            // Byte 3: Device ID
            packet[3] = _deviceId;

            // Bytes 4-7: Data offset (big-endian 32-bit)
            packet[4] = (byte)((deviceOffset >> 24) & 0xFF);
            packet[5] = (byte)((deviceOffset >> 16) & 0xFF);
            packet[6] = (byte)((deviceOffset >> 8) & 0xFF);
            packet[7] = (byte)(deviceOffset & 0xFF);

            // Bytes 8-9: Data length (big-endian 16-bit)
            packet[8] = (byte)((length >> 8) & 0xFF);
            packet[9] = (byte)(length & 0xFF);

            // Bytes 10+: Data payload
            Buffer.BlockCopy(data, sourceOffset, packet, DDP_HEADER_SIZE, length);

            return packet;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _udpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}
