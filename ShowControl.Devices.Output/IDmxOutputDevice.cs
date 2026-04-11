using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Devices.Output
{
    public interface IDmxOutputDevice
    {
        public Task Write(byte[] dmxData);

        public int MaxChannels { get; }
    }
}
