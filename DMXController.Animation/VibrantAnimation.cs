using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public class VibrantAnimation : MidiAwareAnimation
    {
        public override Pixel[] GetNextFrame()
        {
            throw new NotImplementedException();
        }

        public override void MidiAware_OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            throw new NotImplementedException();
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
            throw new NotImplementedException();
        }

        public override void Stop()
        {
            throw new NotImplementedException();
        }
    }
}
