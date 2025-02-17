using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace ShowControl.Devices.DMX
{
    public class ChauvetFreedom : IDmxDevice
    {
        public int ChannelCount => 8;

        public DmxDeviceCapabilities Capabilities => DmxDeviceCapabilities.MasterFader | DmxDeviceCapabilities.RGB | DmxDeviceCapabilities.SoundReactive | DmxDeviceCapabilities.Strobe;

        public byte MasterFader { get; set; }
        public DmxDeviceMode Mode { get; set; }
        public Color Color { get; set; }
        public byte SoundReactivitySensitivity { get; set; }
        public byte StrobeIntensity { get; set; }
        public byte UltraViolet { get; set; }
        public byte White { get; set; }
        public byte Amber { get; set; }

        public ChauvetFreedom()
        {
            Mode = DmxDeviceMode.RGB;
            Color = Color.FromArgb(0, 0, 0, 0);
            MasterFader = 0xFF;
        }

        public static ChauvetFreedom Instance = new ChauvetFreedom();

        private void GetScaledColor()
        {
            //var rPrime = Color.R;
            //var gPrime = Color.G;
            //var bPrime = Color.B;
            //var cMax = Math.Max(rPrime, Math.Max(gPrime, bPrime));
            //var cMin = Math.Min(rPrime, Math.Min(gPrime, bPrime));
            //var delta = cMax - cMin;

            //float hue = Color.GetHue();

            //double saturation;
            //if (cMax == 0)
            //{
            //    saturation = 0;
            //}
            //else
            //{
            //    saturation = 1d - (1d * cMin / cMax);
            //}

            //var value = cMax / 255d;

            //var S = saturation;
            //var V = value * MasterFader / 255;
            //var C = S * V;
            //var X = C * (1 - Math.Abs((hue / 60.0) % 2 - 1));
            //double r, g, b;
            //var m = V - C;
            //if (hue >= 0 && hue < 60)
            //{
            //    r = C;
            //    g = X;
            //    b = 0;
            //}
            //else if (hue >= 60 && hue < 120)
            //{
            //    r = X;
            //    g = C;
            //    b = 0;
            //}
            //else if (hue >= 120 && hue < 180)
            //{
            //    r = 0;
            //    g = C;
            //    b = X;
            //}
            //else if (hue >= 180 && hue < 240)
            //{
            //    r = 0;
            //    g = X;
            //    b = C;
            //}
            //else if (hue >= 240 && hue < 300)
            //{
            //    r = X;
            //    g = 0;
            //    b = C;
            //}
            //else
            //{
            //    r = C;
            //    g = 0;
            //    b = X;
            //}
            //int R = Convert.ToInt32((r + m) * 255);
            //int G = Convert.ToInt32((g + m) * 255);
            //int B = Convert.ToInt32((b + m) * 255);
            //Color = Color.FromArgb(0xFF, R, G, B);
        }

        public byte[] GetDMXVector()
        {
            GetScaledColor();
            if (Mode == DmxDeviceMode.RGB)
            {
                return new byte[] { Math.Min(MasterFader, Color.A), Color.R, Color.G, Color.B, 0x00, 0x00, UltraViolet, 0x00 };
            }
            else if (Mode == DmxDeviceMode.UV)
            {
                return new byte[] { Math.Min(MasterFader, Color.A), 0x00, 0x00, 0x00, 0x00, 0x00, UltraViolet, 0x00 };
            }
            else if(Mode == DmxDeviceMode.SoundReactive)
            {
                return new byte[] { 0xFF, 0x00, 0x00, 0xFF, Color.R, Color.G, Color.B, 0x00, 0x00, 0x00 };
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