using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public class ReadOnlyMidiInputDevice : IReadOnlyMidiInputDevice
    {
        private readonly IReadOnlyMidiInputDevice _inputDevice;
        public ReadOnlyMidiInputDevice(IReadOnlyMidiInputDevice inputDevice)
        {
            _inputDevice = inputDevice;
        }
        int IReadOnlyMidiInputDevice.DeviceId => _inputDevice.DeviceId;

        DeviceType IReadOnlyMidiInputDevice.Type => _inputDevice.Type;

        DeviceCapabilities IReadOnlyMidiInputDevice.Capabilities => _inputDevice.Capabilities;

        int IReadOnlyMidiInputDevice.PadCount => _inputDevice.PadCount;

        int IReadOnlyMidiInputDevice.RotaryEncoderCount => _inputDevice.RotaryEncoderCount;
    }
}
