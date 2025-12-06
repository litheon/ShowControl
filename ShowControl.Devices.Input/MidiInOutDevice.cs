using System;
using System.Collections.Generic;
using System.Text;
using RtMidi.Core;
using RtMidi.Core.Devices.Infos;

namespace ShowControl.Devices.Input
{
    public class MidiInOutDevice
    {
        public IMidiInputDeviceInfo MidiInDeviceInfo { get; private set; }
        public IMidiOutputDeviceInfo MidiOutDeviceInfo { get; private set; }
        public string DeviceName { get; private set; }
        public MidiInOutDevice(IMidiInputDeviceInfo midiInDeviceIndex, IMidiOutputDeviceInfo midiOutDeviceIndex, string deviceName)
        {
            MidiInDeviceInfo = midiInDeviceIndex;
            MidiOutDeviceInfo = midiOutDeviceIndex;
            DeviceName = deviceName;
        }
    }
}
