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
        public byte SoundReactivitySensitivity { get; set; }
        public byte StrobeIntensity { get; set; }
        public byte MasterFader { get; set; }

        public JLPow()
        {
            Mode = DmxDeviceMode.RGB;
            Color = Color.FromArgb(0, 0, 0, 0);
            MasterFader = 0xFF;
        }

        public static JLPow Instance => new JLPow();

        public byte[] GetDMXVector()
        {
            if (Mode == DmxDeviceMode.RGB)
            {
                return new byte[] { MasterFader, Color.R, Color.G, Color.B, 0x00, 0x00 };
            }
            else if (Mode == DmxDeviceMode.SoundReactive)
            {
                return new byte[] { MasterFader, Color.R, Color.G, Color.B, 0x00, SoundReactivitySensitivity };
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
