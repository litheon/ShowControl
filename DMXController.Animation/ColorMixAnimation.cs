using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using ShowControl.Utilities;

namespace ShowControl.Animation
{
    public class ColorMixAnimation : MidiAwareAnimation
    {
        int R;
        int G;
        int B;
        Pixel[] _rootFrame;


        public override bool IsActive { get; set; }

        public ColorMixAnimation(int pixelCount)
        {
            _rootFrame = new Pixel[pixelCount];
            for (int i = 0; i < _rootFrame.Length; i++)
            {
                _rootFrame[i] = new Pixel(System.Drawing.Color.FromArgb(0x00, 0x00, 0x00, 0x00), 0x00, 0x00);
            }
        }

        public override Pixel[] GetNextFrame()
        {
            int r;
            int g;
            int b;
            {
                r = R.InterlockedGet();
                g = G.InterlockedGet();
                b = B.InterlockedGet();
            }
            for (int i = 0; i < _rootFrame.Length; i++)
            {
                _rootFrame[i].Color = System.Drawing.Color.FromArgb(0xFF, r, g, b);
            }
            return _rootFrame;
        }

        public override void MidiAware_OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            if (e.EventType == DeviceInputEventType.RotaryEncoder)
            {
                switch (e.RotaryNumber)
                {
                    case 1:
                        _ = e.RotaryDirection == RotaryDirection.Up ? R.InterlockedIncrement(byte.MinValue, byte.MaxValue) : R.InterlockedDecrement(byte.MinValue, byte.MaxValue);
                        break;
                    case 2:
                        _ = e.RotaryDirection == RotaryDirection.Up ? G.InterlockedIncrement(byte.MinValue, byte.MaxValue) : G.InterlockedDecrement(byte.MinValue, byte.MaxValue);
                        break;
                    case 3:
                        _ = e.RotaryDirection == RotaryDirection.Up ? B.InterlockedIncrement(byte.MinValue, byte.MaxValue) : B.InterlockedDecrement(byte.MinValue, byte.MaxValue);
                        break;
                }
            }
        }

        public override DeviceInitializationState GetDeviceInitializationState(IMidiInputDevice inputDevice)
        {
            return new DeviceInitializationState(
                new System.Drawing.Color[0],
                new ButtonLightState[1]
                {
                    new ButtonLightState(Button.Up, LightState.On)
                }
            );
        }
    }
}
