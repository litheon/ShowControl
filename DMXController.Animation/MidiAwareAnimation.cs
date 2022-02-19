using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public abstract class MidiAwareAnimation : IAnimation, IMidiAware
    {
        public event EventHandler<DeviceStateChangeEventArgs> MidiAware_DeviceStateChangeEvent;

        public abstract Pixel[] GetNextFrame();
        public abstract void MidiAware_OnMessageReceived(object sender, DeviceInputEventArgs e);
        public abstract DeviceState Start(IReadOnlyMidiInputDevice inputDevice);
        public abstract void Stop();

        internal void SetPadColor(int index, System.Drawing.Color color)
        {
            MidiAware_DeviceStateChangeEvent?.Invoke(this, new DeviceStateChangeEventArgs(index, color));
        }

        internal void SetNavigationColor(System.Drawing.Color navigationColor)
        {
            MidiAware_DeviceStateChangeEvent?.Invoke(this, new DeviceStateChangeEventArgs(navigationColor));
        }
    }
}
