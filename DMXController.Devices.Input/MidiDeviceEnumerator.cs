using System;
using System.Collections.Generic;
using System.Text;
using NAudio.Midi;

namespace ShowControl.Devices.Input
{
    public class MidiDeviceEnumerator
    {
        public static MidiInOutDevice[] GetMidiDevices()
        {
            Dictionary<string, Tuple<int, MidiOutCapabilities>> midiOutDevices = new Dictionary<string, Tuple<int, MidiOutCapabilities>>();
            Dictionary<string, Tuple<int, MidiInCapabilities>> midiInDevices = new Dictionary<string, Tuple<int, MidiInCapabilities>>();
            var midiInDeviceCount = MidiIn.NumberOfDevices;
            for (int i = 0; i < midiInDeviceCount; i++)
            {
                var device = MidiIn.DeviceInfo(i);
                midiInDevices.Add(device.ProductName, new Tuple<int, MidiInCapabilities>(i, device));
            }

            var midiOutDeviceCount = MidiOut.NumberOfDevices;
            for (int i = 0; i < midiOutDeviceCount; i++)
            {
                var device = MidiOut.DeviceInfo(i);
                midiOutDevices.Add(device.ProductName, new Tuple<int, MidiOutCapabilities>(i, device));
            }
            List<MidiInOutDevice> devices = new List<MidiInOutDevice>(midiOutDevices.Count);
            foreach (var kvPair in midiOutDevices)
            {
                if (midiInDevices.ContainsKey(kvPair.Key))
                {
                    var productName = kvPair.Value.Item2.ProductName;
                    var outIndex = kvPair.Value.Item1;
                    var inIndex = midiInDevices[kvPair.Key].Item1;
                    devices.Add(new MidiInOutDevice(inIndex, outIndex, productName));
                }
            }
            return devices.ToArray();
        }
    }
}
