using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public class MirrorTransformAnimation : MidiAwareAnimation, ITransformAnimation
    {
        private Pixel[] _keyFrameBuffer;
        public Pixel[] GetNextFrame(Pixel[] keyFrame)
        {
            if (_keyFrameBuffer == null || _keyFrameBuffer.Length != keyFrame.Length)
            {
                _keyFrameBuffer = new Pixel[keyFrame.Length];
            }
            for (int i = 0; i < keyFrame.Length / 2; i++)
            {
                _keyFrameBuffer[i] = keyFrame[i];
                _keyFrameBuffer[^(i+1)] = keyFrame[i];
            }
            return _keyFrameBuffer;
        }

        public override void OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            //throw new NotImplementedException();
        }

        public override void PadPressed(ButtonDirection direction, byte padNumber, int padCount)
        {
            //throw new NotImplementedException();
        }

        public override void RotaryTurned(RotaryDirection direction, byte rotaryNumber, byte? value)
        {
            //throw new NotImplementedException();
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
            return new DeviceState(new PadState[0], new ButtonLightState[0]);
            //throw new NotImplementedException();
        }

        public override void Stop()
        {
            //throw new NotImplementedException();
        }
    }
}
