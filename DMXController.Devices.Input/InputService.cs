using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using RtMidi.Core.Devices.Infos;

namespace ShowControl.Devices.Input
{
    public partial class InputService : IInputService
    {
        private bool _initialized = false;
        // Need some way of receiving device connected events on Mac and PC

        public event EventHandler<DeviceInputEventArgs> OnInputReceived;
        public event EventHandler OnInputDeviceReconnected;

        private List<IMidiInputDevice> _devices;
        private Dictionary<string, List<IMidiInputDevice>> _deviceDictionary;

        private readonly Dictionary<string, Func<IMidiInputDeviceInfo, IMidiOutputDeviceInfo, int, IMidiInputDevice>> _supportedDevices = new Dictionary<string, Func<IMidiInputDeviceInfo, IMidiOutputDeviceInfo, int, IMidiInputDevice>>() {
            { "ATOM", (i, o, dId) => new PresonusAtom(dId, i, o) }
        };

        public InputService()
        {

            _deviceDictionary = new Dictionary<string, List<IMidiInputDevice>>();
            ConstructMidiDevices();
            _devices = new List<IMidiInputDevice>(_deviceDictionary.SelectMany(d => d.Value));
            #if WINDOWS
            PlatformInitialization();
            #endif
            #if ARM64
            PlatformInitialization();
            #endif
        }

        public IEnumerable<IMidiInputDevice> Devices
        {
            get
            {
                lock (_devices)
                {
                    return _devices;
                }
            }
        }

        private void ConstructMidiDevices()
        {
            var midiInDevices = MidiDeviceEnumerator.GetMidiDevices();
            for (int i = 0; i < midiInDevices.Length; i++)
            {
                var key = midiInDevices[i].DeviceName.Trim();
                if (_supportedDevices.ContainsKey(key))
                {
                    var device = midiInDevices[i];
                    if (_initialized && _deviceDictionary.TryGetValue(key, out var midiInputDevices))
                    {
                        midiInputDevices.First().Reconnect(device.MidiInDeviceInfo, device.MidiOutDeviceInfo);
                    }
                    else
                    {
                        var constructor = _supportedDevices[key];
                        var constructedDevice = constructor(device.MidiInDeviceInfo, device.MidiOutDeviceInfo, i);
                        constructedDevice.OnInputReceived += ConstructedDevice_OnInputReceived;
                        if (!_deviceDictionary.ContainsKey(key))
                        {
                            _deviceDictionary.Add(key, new List<IMidiInputDevice>());
                        }
                        _deviceDictionary[key].Add(constructedDevice);
                    }
                }
            }
            _initialized = true;
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
