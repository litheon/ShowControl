using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public class WaveDim : MidiAwareAnimation, ITransformAnimation
    {
        private Pixel[] _nextFrame;

        private const double _waveWidthMin = 30;
        private const double _waveWidthMax = 10000;
        private double _waveWidth = 1000;
        private int _waveOffset = 0;

        private int _waveCountMin = 1;
        private int _waveCountMax;
        private int _waveCount = 1;

        private const double _waveDepthMin = 1;
        private const double _waveDepthMax = 2;
        private double _waveDepth = 1.2;

        public Pixel[] GetNextFrame(Pixel[] keyFrame)
        {
            if (_nextFrame?.Length != keyFrame.Length)
            {
                _nextFrame = new Pixel[keyFrame.Length];
                _waveCountMax = keyFrame.Length / 4;
            }

            for (int i = 0; i < keyFrame.Length; i++)
            {
                var brightnessMultiplier = 1 - Math.Abs(Math.Sin((Convert.ToDouble(i + 1) / (keyFrame.Length) + _waveOffset / _waveWidth) * 2 * _waveCount * Math.PI )) / _waveDepth;
                var brightness = Math.Min(keyFrame[i].Color.A, Convert.ToByte(byte.MaxValue * brightnessMultiplier));
                _nextFrame[i] = keyFrame[i];
                _nextFrame[i].Color = System.Drawing.Color.FromArgb(brightness, keyFrame[i].Color);
            }

            _waveOffset++;

            if (_waveOffset >= _waveWidth)
            {
                _waveOffset = 0;
            }

            return _nextFrame;
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
            return new DeviceState(new PadState[0], new ButtonLightState[0]);
        }

        public override void Stop() { }

        public override void OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            
        }

        public override void RotaryTurned(RotaryDirection direction, byte rotaryNumber, byte? value)
        {
            if (rotaryNumber == 2)
            {
                //Wave width
                _waveWidth += 10d * (direction == RotaryDirection.Up ? 1 : -1);
                if (_waveWidth < _waveWidthMin)
                {
                    _waveWidth = _waveWidthMin;
                }
                else if (_waveWidth > _waveWidthMax)
                {
                    _waveWidth = _waveWidthMax;
                }
            }
            else if(rotaryNumber == 3)
            {
                //Wave count
                _waveCount += (direction == RotaryDirection.Up ? 1 : -1);
                if (_waveCount < _waveCountMin)
                {
                    _waveCount = _waveCountMin;
                }
                else if (_waveCount > _waveCountMax)
                {
                    _waveCount = _waveCountMax;
                }
            }
            else if (rotaryNumber == 4)
            {
                //Wave width
                _waveDepth += .05d * (direction == RotaryDirection.Up ? 1 : -1);
                if (_waveDepth < _waveDepthMin)
                {
                    _waveDepth = _waveDepthMin;
                }
                else if (_waveDepth > _waveDepthMax)
                {
                    _waveDepth = _waveDepthMax;
                }
            }
        }

        public override void PadPressed(ButtonDirection direction, byte padNumber, int padCount)
        {
            
        }
    }
}
