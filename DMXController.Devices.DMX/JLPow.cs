using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace ShowControl.Devices.DMX
{
    public class JLPow : IDmxDevice
    {
        public int ChannelCount => 6;

        public DmxDeviceCapabilities Capabilities => DmxDeviceCapabilities.MasterFader | DmxDeviceCapabilities.RGB | DmxDeviceCapabilities.SoundReactive | DmxDeviceCapabilities.Strobe;

        public DmxDeviceMode Mode { get; set; }
        public Color Color { get; set; }
        public JLPow()
        {
            Mode = DmxDeviceMode.RGB;
            Color = Color.FromArgb(0, 0, 0, 0);
        }

        public byte[] GetDMXVector()
        {
            if (Mode == DmxDeviceMode.RGB)
            {
                return new byte[] { Color.A, Color.R, Color.G, Color.B, 0x00, 0x00 };
            }
            else if (Mode == DmxDeviceMode.SoundReactive)
            {
                return new byte[] { Color.A, Color.R, Color.G, Color.B, 0x00, 0xFF };
            }
            else if (Mode == DmxDeviceMode.Off)
            {
                return new byte[] { 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00 };
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}
