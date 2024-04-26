using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public struct ButtonState
    {
        public Button Button { get; set; }
        public LightState LightState { get; set; }
        public ButtonState(Button button, LightState lightState)
        {
            Button = button;
            LightState = lightState;
        }
    }
}
