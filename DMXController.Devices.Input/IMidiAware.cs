using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public interface IMidiAware
    {
        public void MidiAware_OnMessageReceived(object sender, DeviceInputEventArgs e);

        public event EventHandler<DeviceOutputEventArgs> MidiAware_DeviceOutputMessage;
    }
}
