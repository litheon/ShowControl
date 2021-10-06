using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public abstract class MidiAwareAnimation : IAnimation, IMidiAware
    {
        public abstract bool IsActive { get; set; }

        public abstract event EventHandler<DeviceOutputEventArgs> MidiAware_DeviceOutputMessage;

        public abstract Pixel[] GetNextFrame();
        public abstract void MidiAware_OnMessageReceived(object sender, DeviceInputEventArgs e);
    }
}
