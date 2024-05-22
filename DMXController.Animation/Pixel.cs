using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public struct Pixel
    {
        public System.Drawing.Color Color { get; set; }
        public byte Strobe { get; }
        public byte SoundReactivity { get; }
        public byte UV { get; }

        public Pixel(System.Drawing.Color color, byte strobe, byte soundReactivity, byte uv)
        {
            Color = color;
            Strobe = strobe;
            SoundReactivity = soundReactivity;
            UV = uv;
        }
    }
}
