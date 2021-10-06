using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    [Flags]
    public enum DeviceCapabilities
    {
        Pads = 0x0001,
        RotaryEncoders = 0x0002,
        Transport = 0x0004,
        ButtonIllumination = 0x0008,
        Navigation = 0xC000
    }
}
