using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace ShowControl.Devices.Input
{
    public class DeviceStateTracker : IDeviceStateTracker
    {
        public void GetDeviceState(IMidiInputDevice device)
        {
            throw new NotImplementedException();
        }

        public void SetButtonIllumination(int button, LightState lightState)
        {
            throw new NotImplementedException();
        }

        public void SetPadColor(int pad, Color color)
        {
            throw new NotImplementedException();
        }
    }
}
