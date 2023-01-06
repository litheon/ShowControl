using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;

namespace ShowControl.Devices.Input
{
    public enum PadIllumination { Off, Blink, Breathe, Solid}
    public struct PadState
    {
        public PadIllumination PadIllumination { get; set; }
        public Color Color { get; set; }
        public PadState(PadIllumination padIllumination, Color color)
        {
            PadIllumination = padIllumination;
            Color = color;
        }
    }
}
