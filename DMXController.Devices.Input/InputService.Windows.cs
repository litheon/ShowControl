using System;
using System.Collections.Generic;
using System.Text;
#if WINDOWS_TFM
using Windows.Devices.Midi;
using Windows.Devices.Enumeration;
#endif
using System.Linq;

namespace ShowControl.Devices.Input
{
    #if WINDOWS_TFM
    public partial class InputService
    {
        private DeviceWatcher _watcher;

        private void PlatformInitialization()
        {
            var selector = MidiInPort.GetDeviceSelector();
            _watcher = DeviceInformation.CreateWatcher(selector);
            _watcher.Added += _watcher_Added;
            _watcher.Removed += _watcher_Removed;
            _watcher.Updated += _watcher_Updated;
            _watcher.Start();
        }


        private void _watcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
        {

        }

        private void _watcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
        {

        }

        private void _watcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            if (_initialized)
            {
                lock (_devices)
                {
                    ConstructMidiDevices();
                    _devices = new List<IMidiInputDevice>(_deviceDictionary.SelectMany(d => d.Value));
                }

                OnInputDeviceReconnected?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    #endif
}
