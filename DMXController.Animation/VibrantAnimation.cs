using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public class VibrantAnimation : MidiAwareAnimation
    {
        public override bool IsActive { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override event EventHandler<DeviceInitializationState> MidiAware_DeviceOutputMessage;

        public override Pixel[] GetNextFrame()
        {
            throw new NotImplementedException();
        }

        public override void MidiAware_OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            throw new NotImplementedException();
        }

        public override DeviceInitializationState GetDeviceInitializationState(IMidiInputDevice inputDevice)
        {
            throw new NotImplementedException();
        }
    }
}
