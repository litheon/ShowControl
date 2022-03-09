using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public class InputService : IInputService
    {
        public event EventHandler<DeviceInputEventArgs> OnInputReceived;

        private readonly Dictionary<string, Func<int, int, int, IMidiInputDevice>> _supportedDevices = new Dictionary<string, Func<int, int, int, IMidiInputDevice>>() {
            { "ATOM", (dId, iId, oId) => new PresonusAtom(dId, iId, oId) }
        };

        public InputService()
        {
            Devices = new List<IMidiInputDevice>();
            ConstructMidiDevices();
        }

        public List<IMidiInputDevice> Devices { get; }

        private void ConstructMidiDevices()
        {
            var midiInDevices = MidiDeviceEnumerator.GetMidiDevices();
            for (int i = 0; i < midiInDevices.Length; i++)
            {
                if (_supportedDevices.ContainsKey(midiInDevices[i].DeviceName))
                {
                    var device = midiInDevices[i];
                    var constructor = _supportedDevices[device.DeviceName];
                    var constructedDevice = constructor(i, device.MidiInDeviceIndex, device.MidiOutDeviceIndex);
                    constructedDevice.OnInputReceived += ConstructedDevice_OnInputReceived;
                    Devices.Add(constructedDevice);
                }
            }
        }

        private void ConstructedDevice_OnInputReceived(object sender, DeviceInputEventArgs e)
        {
            OnInputReceived?.Invoke(e.Source, e);
        }

        public void Stop()
        {
            foreach (var device in Devices)
            {
                device.Stop();
            }
        }
    }
}
