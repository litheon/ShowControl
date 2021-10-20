using ShowControl.Devices.Input;
using System;

namespace ShowControl.Animation
{
    public interface IAnimation
    {
        public DeviceInitializationState GetDeviceInitializationState(IMidiInputDevice inputDevice);
        public Pixel[] GetNextFrame();
    }
}
