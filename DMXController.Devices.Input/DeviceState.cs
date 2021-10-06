using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public class DeviceState
    {
        public readonly Dictionary<int, LightState> LightStates;
        public readonly Dictionary<int, System.Drawing.Color> PadStates;
        public DeviceState(Dictionary<int, LightState> lightStates, Dictionary<int, System.Drawing.Color> padStates)
        {
            LightStates = new Dictionary<int, LightState>(lightStates);
            PadStates = new Dictionary<int, System.Drawing.Color>(padStates);
        }
    }
}
