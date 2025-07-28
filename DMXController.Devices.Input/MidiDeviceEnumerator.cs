using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using NAudio.Midi;
using RtMidi.Core;
using RtMidi.Core.Devices.Infos;
using System.Runtime.InteropServices;

namespace ShowControl.Devices.Input
{
    public class MidiDeviceEnumerator
    {
        public static MidiInOutDevice[] GetMidiDevices()
        {
            var a = MidiDeviceManager.Default.GetAvailableMidiApis();
            var defaulta = MidiDeviceManager.Default;
            //GetPortCount(0u);
            var inDevices = defaulta.InputDevices.ToArray();
            var count = inDevices.Count();
            var outDevices = MidiDeviceManager.Default.OutputDevices.ToArray();
            Dictionary<string, IMidiOutputDeviceInfo> midiOutDevices = new Dictionary<string, IMidiOutputDeviceInfo>();
            Dictionary<string, IMidiInputDeviceInfo> midiInDevices = new Dictionary<string, IMidiInputDeviceInfo>();
            for (int i = 0; i < inDevices.Length; i++)
            {
                var device = inDevices[i];
                midiInDevices.Add(device.Name, device);
            }

            for (int i = 0; i < outDevices.Length; i++)
            {
                var device = outDevices[i];
                midiOutDevices.Add(device.Name, device);
            }
            List<MidiInOutDevice> devices = new List<MidiInOutDevice>(midiOutDevices.Count);
            foreach (var kvPair in midiOutDevices)
            {
                if (midiInDevices.ContainsKey(kvPair.Key))
                {
                    var outDevice = kvPair.Value;
                    var inDevice = midiInDevices[kvPair.Key];
                    devices.Add(new MidiInOutDevice(inDevice, outDevice, outDevice.Name));
                }
            }
            return devices.ToArray();
        }
    }
}
