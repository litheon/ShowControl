using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public class MidiInOutDevice
    {
        public int MidiInDeviceIndex { get; private set; }
        public int MidiOutDeviceIndex { get; private set; }
        public string DeviceName { get; private set; }
        public MidiInOutDevice(int midiInDeviceIndex, int midiOutDeviceIndex, string deviceName)
        {
            MidiInDeviceIndex = midiInDeviceIndex;
            MidiOutDeviceIndex = midiOutDeviceIndex;
            DeviceName = deviceName;
        }
    }
}
