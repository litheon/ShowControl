using System;
using System.Collections.Generic;
using System.Text;
//using Windows.Devices.Midi;
//using Windows.Devices.Enumeration;
using System.Linq;
using RtMidi.Core.Devices.Infos;

namespace ShowControl.Devices.Input
{
    public class InputService : IInputService
    {
        private bool _initialized = false;
        //private DeviceWatcher _watcher;
        public event EventHandler<DeviceInputEventArgs> OnInputReceived;
        public event EventHandler OnInputDeviceReconnected;

        private List<IMidiInputDevice> _devices;
        private Dictionary<string, List<IMidiInputDevice>> _deviceDictionary;

        private readonly Dictionary<string, Func<IMidiInputDeviceInfo, IMidiOutputDeviceInfo, int, IMidiInputDevice>> _supportedDevices = new Dictionary<string, Func<IMidiInputDeviceInfo, IMidiOutputDeviceInfo, int, IMidiInputDevice>>() {
            { "ATOM", (i, o, dId) => new PresonusAtom(dId, i, o) }
        };

        public InputService()
        {
            //var selector = MidiInPort.GetDeviceSelector();
            //_watcher = DeviceInformation.CreateWatcher(selector);
            //_watcher.Added += _watcher_Added;
            //_watcher.Removed += _watcher_Removed;
            //_watcher.Updated += _watcher_Updated;
            //_watcher.Start();
            _deviceDictionary = new Dictionary<string, List<IMidiInputDevice>>();
            ConstructMidiDevices();
            _devices = new List<IMidiInputDevice>(_deviceDictionary.SelectMany(d => d.Value));
        }

        //private void _watcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
        //{
            
        //}

        //private void _watcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
        //{
            
        //}

        //private void _watcher_Added(DeviceWatcher sender, DeviceInformation args)
        //{
        //    if (_initialized)
        //    {
        //        lock(_devices)
        //        {
        //            ConstructMidiDevices();
        //            _devices = new List<IMidiInputDevice>(_deviceDictionary.SelectMany(d => d.Value));
        //        }

        //        OnInputDeviceReconnected?.Invoke(this, EventArgs.Empty);
        //    }
        //}

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
                if (_supportedDevices.ContainsKey(midiInDevices[i].DeviceName))
                {
                    var device = midiInDevices[i];
                    if (_initialized && _deviceDictionary.TryGetValue(device.DeviceName, out var midiInputDevices))
                    {
                        midiInputDevices.First().Reconnect(device.MidiInDeviceInfo, device.MidiOutDeviceInfo);
                    }
                    else
                    {
                        var constructor = _supportedDevices[device.DeviceName];
                        var constructedDevice = constructor(device.MidiInDeviceInfo, device.MidiOutDeviceInfo, i);
                        constructedDevice.OnInputReceived += ConstructedDevice_OnInputReceived;
                        if (!_deviceDictionary.ContainsKey(device.DeviceName))
                        {
                            _deviceDictionary.Add(device.DeviceName, new List<IMidiInputDevice>());
                        }
                        _deviceDictionary[device.DeviceName].Add(constructedDevice);
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
