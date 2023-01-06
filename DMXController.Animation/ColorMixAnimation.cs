using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using ShowControl.Utilities;

namespace ShowControl.Animation
{
    public class ColorMixAnimation : MidiAwareAnimation, IKeyAnimation
    {
        private int R;
        private int G;
        private int B;
        private readonly Pixel[] _rootFrame;

        public ColorMixAnimation(int pixelCount)
        {
            _rootFrame = new Pixel[pixelCount];
            for (int i = 0; i < _rootFrame.Length; i++)
            {
                _rootFrame[i] = new Pixel(System.Drawing.Color.FromArgb(0x00, 0x00, 0x00, 0x00), 0x00, 0x00);
            }
        }

        public Pixel[] GetNextFrame()
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

        public override void OnMessageReceived(object sender, DeviceInputEventArgs e)
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

        public override void PadPressed(ButtonDirection direction, byte padNumber, int padCount)
        {
            
        }

        public override void RotaryTurned(RotaryDirection direction, byte rotaryNumber, byte? value)
        {
            
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
            return new DeviceState(
                new PadState[0],
                new ButtonLightState[1]
                {
                    new ButtonLightState(Button.Up, LightState.On)
                }
            );
        }

        public override void Stop() { }
    }
}
