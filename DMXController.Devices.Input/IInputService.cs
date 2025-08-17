using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public interface IInputService
    {
        public event EventHandler OnInputDeviceReconnected;
        public event EventHandler<DeviceInputEventArgs> OnInputReceived;
        public IEnumerable<IMidiInputDevice> Devices { get; }

        public void DoPlatformInitalization();
        public void Stop();
    }
}
