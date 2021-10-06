using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public struct Pixel
    {
        public System.Drawing.Color Color { get; }
        public byte Strobe { get; }
        public byte SoundReactivity { get; }
        public Pixel(System.Drawing.Color color, byte strobe, byte soundReactivity)
        {
            Color = color;
            Strobe = strobe;
            SoundReactivity = soundReactivity;
        }
    }
}
