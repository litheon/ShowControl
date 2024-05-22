using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace ShowControl.Devices.DMX
{
    public class BetopperDJ : IDmxDevice
    {
        public int ChannelCount => 7;

        public DmxDeviceCapabilities Capabilities => DmxDeviceCapabilities.MasterFader | DmxDeviceCapabilities.RGB | DmxDeviceCapabilities.SoundReactive | DmxDeviceCapabilities.Strobe;

        public DmxDeviceMode Mode { get; set; }
        public Color Color { get; set; }
        public byte SoundReactivitySensitivity { get; set; }
        public byte StrobeIntensity { get; set; }
        public byte MasterFader { get; set; }
        public byte UltraViolet { get; set; }
        public byte White { get; set; }
        public byte Amber { get; set; }


        public BetopperDJ()
        {
            Mode = DmxDeviceMode.RGB;
            Color = Color.FromArgb(0, 0, 0, 0);
            MasterFader = 0xFF;
        }

        public byte[] GetDMXVector()
        {
            if (Mode == DmxDeviceMode.RGB)
            {
                return new byte[] { MasterFader, Color.R, Color.G, Color.B, 0x00, 0x00, 0x00 };
                //return new byte[] { MasterFader, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00 };
            }
            else if(Mode == DmxDeviceMode.SoundReactive)
            {
                return new byte[] { MasterFader, Color.R, Color.G, Color.B, 0x00, 0xFF, 0xFF };
            }
            else if (Mode == DmxDeviceMode.Strobe)
            {
                return new byte[] { MasterFader, Color.R, Color.G, Color.B, StrobeIntensity, 0xFF, 0x00 };
            }
            else if (Mode == DmxDeviceMode.Off)
            {
                return new byte[] { 0x00, Color.R, Color.G, Color.B, 0x00, 0x00, 0x00 };
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}
