using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public class ColorMixAnimation : MidiAwareAnimation
    {
        IMidiInputDevice _device;
        byte R;
        byte G;
        byte B;
        Pixel[] _rootFrame;

        object _colorLock = new object();

        public override event EventHandler<DeviceInitializationState> MidiAware_DeviceOutputMessage;

        public override bool IsActive { get; set; }

        public ColorMixAnimation(int pixelCount)
        {
            _rootFrame = new Pixel[pixelCount];
        }

        public override Pixel[] GetNextFrame()
        {
            lock(_colorLock)
            {
                for (int i = 0; i < _rootFrame.Length; i++)
                {
                    _rootFrame[i] = new Pixel(System.Drawing.Color.FromArgb(0xFF, R, G, B), 0x00, 0x00);
                }
            }
            return _rootFrame;
        }

        public override void MidiAware_OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            if (e.EventType == DeviceInputEventType.RotaryEncoder)
            {
                lock (_colorLock)
                {
                    switch (e.RotaryNumber)
                    {
                        case 14:
                            R = (byte)(e.RotaryDirection == RotaryDirection.Up ? (R + 1) % 255 : (R - 1) % 255);
                            break;
                        case 15:
                            G = (byte)(e.RotaryDirection == RotaryDirection.Up ? (G + 1) % 255 : (G - 1) % 255);
                            break;
                        case 16:
                            B = (byte)(e.RotaryDirection == RotaryDirection.Up ? (B + 1) % 255 : (B - 1) % 255);
                            break;
                    }
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
