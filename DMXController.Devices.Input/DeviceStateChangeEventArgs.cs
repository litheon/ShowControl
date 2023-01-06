using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public enum DeviceStateChangeEventType { PadColor, NavigationColor };
    public class DeviceStateChangeEventArgs : EventArgs
    {
        public DeviceStateChangeEventType EventType { get; private set; }

        public int PadNumber { get; private set; }
        public PadState PadState { get; private set; }

        public System.Drawing.Color NavigationColor { get; private set; }

        public DeviceStateChangeEventArgs(int padNumber, PadState padState)
        {
            EventType = DeviceStateChangeEventType.PadColor;
            PadNumber = padNumber;
            PadState = padState;
        }

        public DeviceStateChangeEventArgs(System.Drawing.Color navigationColor)
        {
            EventType = DeviceStateChangeEventType.NavigationColor;
            NavigationColor = navigationColor;
        }
    }
}
