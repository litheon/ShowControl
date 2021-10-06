using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public interface IInputService
    {
        public event EventHandler<DeviceInputEventArgs> OnInputReceived;
        public List<IMidiInputDevice> Devices { get; }
    }
}
