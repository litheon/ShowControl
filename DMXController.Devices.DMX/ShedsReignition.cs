using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace ShowControl.Devices.DMX
{
    public class ShedsReignition : IDmxDevice
    {
        public int ChannelCount => 10;

        public DmxDeviceCapabilities Capabilities => DmxDeviceCapabilities.MasterFader | DmxDeviceCapabilities.RGB | DmxDeviceCapabilities.SoundReactive | DmxDeviceCapabilities.Strobe;

        public byte MasterFader { get; set; }
        public DmxDeviceMode Mode { get; set; }
        public Color Color { get; set; }
        public byte SoundReactivitySensitivity { get; set; }
        public byte StrobeIntensity { get; set; }

        public ShedsReignition()
        {
            Mode = DmxDeviceMode.RGB;
            Color = Color.FromArgb(0, 0, 0, 0);
            MasterFader = 0xFF;
        }

        public byte[] GetDMXVector()
        {
            if (Mode == DmxDeviceMode.RGB)
            {
                return new byte[] { MasterFader, Color.R, Color.G, Color.B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                //return new byte[] { MasterFader, 0x00, 0x00, 0x00, 0XFF, 0XFF, 0XFF };
            }
            else if(Mode == DmxDeviceMode.SoundReactive)
            {
                return new byte[] { 0x00, Color.R, Color.G, Color.B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            }
            else if (Mode == DmxDeviceMode.Strobe)
            {
                throw new NotImplementedException();
            }
            else if (Mode == DmxDeviceMode.Off)
            {
                return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}
