using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input.Extensions
{
    public static class IMidiInputDeviceExtensions
    {
        public static IReadOnlyMidiInputDevice AsReadOnly(this IMidiInputDevice device)
        {
            return new ReadOnlyMidiInputDevice(device);
        }
    }
}
