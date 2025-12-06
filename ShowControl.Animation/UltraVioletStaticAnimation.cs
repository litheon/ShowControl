using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using ShowControl.Utilities;

namespace ShowControl.Animation
{
    public class UltraVioletStaticAnimation : MidiAwareAnimation, IKeyAnimation
    {
        private int R;
        private int G;
        private int B;
        private readonly Pixel[] _rootFrame;

        public UltraVioletStaticAnimation(int pixelCount)
        {
            _rootFrame = new Pixel[pixelCount];
            for (int i = 0; i < _rootFrame.Length; i++)
            {
                _rootFrame[i] = new Pixel(System.Drawing.Color.FromArgb(0xFF, 0x00, 0x00, 0x00), 0x00, 0x00, 0xFF);
            }
            SetNavigationColor(System.Drawing.Color.Violet);
        }

        public Pixel[] GetNextFrame()
        {
            return _rootFrame;
        }

        public override void OnMessageReceived(object sender, DeviceInputEventArgs e)
        {

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
