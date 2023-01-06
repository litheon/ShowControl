using ShowControl.Devices.Input;
using System;

namespace ShowControl.Animation
{
    public interface IAnimation
    {
        public void Stop();
        public DeviceState Start(IReadOnlyMidiInputDevice inputDevice);
    }
}
