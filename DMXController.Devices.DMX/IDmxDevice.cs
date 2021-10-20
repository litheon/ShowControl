using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace ShowControl.Devices.DMX
{
    public interface IDmxDevice
    {
        public int ChannelCount { get; }
        public DmxDeviceCapabilities Capabilities { get; }
        public DmxDeviceMode Mode { get; set; }
        public byte[] GetDMXVector();
        public Color Color { get; set; }
        public byte SoundReactivitySensitivity { get; set; }
    }
}
