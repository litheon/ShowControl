using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Output
{
    public interface IDmxOutputDevice
    {
        public void Write(byte[] dmxData);
    }
}
