using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Devices.Output
{
    public class ArtNetDevice : IDmxOutputDevice
    {
        private readonly IArtNetController _controller;
        private readonly byte _universe;
        private readonly byte[] _ipAddress;
        private byte _sequence = 0;

        public int MaxChannels { get; private set; }

        public ArtNetDevice(IArtNetController controller, byte universe, byte[] ipAddress, int maxChannels)
        {
            _controller = controller;
            _universe = universe;
            _ipAddress = ipAddress;
            MaxChannels = maxChannels;
        }

        public async Task Write(byte[] dmxData)
        {
            await _controller.SendArtDMX(_ipAddress, _universe, 0, dmxData, _sequence);
            _sequence = (byte)(((_sequence) % 254) + 1);
        }
    }
}
