using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public enum LightState { On, Off };
    public struct ButtonLightState
    {
        public LightState LightState;
        public Button Button;
        public ButtonLightState(Button button, LightState lightState)
        {
            LightState = lightState;
            Button = button;
        }
    }
    public class DeviceInitializationState
    {
        public ButtonLightState[] ButtonLightStates { get; private set; }
        public System.Drawing.Color[] PadColors { get; private set; }
        public DeviceInitializationState(System.Drawing.Color[] padColors, ButtonLightState[] buttonLightStates)
        {
            PadColors = padColors;
            ButtonLightStates = buttonLightStates;
        }
    }
}
