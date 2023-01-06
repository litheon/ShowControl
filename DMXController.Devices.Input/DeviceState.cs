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
    public class DeviceState
    {
        public ButtonLightState[] ButtonLightStates { get; private set; }
        public PadState[] PadStates { get; private set; }
        public DeviceState(PadState[] padStates, ButtonLightState[] buttonLightStates)
        {
            PadStates = padStates;
            ButtonLightStates = buttonLightStates;
        }
    }
}
