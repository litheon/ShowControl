using System;

namespace ShowControl.Devices.DMX
{
    [Flags]
    public enum DmxDeviceCapabilities
    {
        MasterFader = 0x00000001,
        RGB = 0x00000002,
        Strobe = 0x00000004,
        SoundReactive = 0x00000008
    };

    public enum DmxDeviceMode
    {
        RGB,
        UV,
        Strobe,
        SoundReactive,
        Off
    };
}
