using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public interface IDeviceStateTracker
    {
        public void SetPadColor(int pad, System.Drawing.Color color);
        public void SetButtonIllumination(int button, LightState lightState);
        public void GetDeviceState(IMidiInputDevice device);
    }
}
