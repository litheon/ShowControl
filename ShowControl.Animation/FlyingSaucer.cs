using ShowControl.Devices.Audio;
using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace ShowControl.Animation
{
    public class FlyingSaucer : BpmSynchronizedAnimation, ITransformAnimation
    {
        private byte[] _source;
        private byte[] _target;
        private byte[] _current;

        private int _frameSkipProgress;
        private const int FrameSkipCountMultiplier = 8;

        private int _onLightMax;
        private int _onLightMin = 0;
        private int _onLightCount = 3;

        private int _numLights;

        private object _memberLock = new object();

        private SmoothAlphaTransition _smoothAlphaTransition;

        public FlyingSaucer(
            IAudioService audioService,
            CancellationToken threadCancellationToken)
            : base(audioService, threadCancellationToken)
        {
            _smoothAlphaTransition = new SmoothAlphaTransition();
        }

        public Pixel[] GetNextFrame(Pixel[] keyFrame)
        {
            lock (_memberLock)
            {
                if (_source?.Length != keyFrame.Length)
                {
                    _numLights = keyFrame.Length;
                    _source = new byte[keyFrame.Length];
                    _target = new byte[keyFrame.Length];
                    _current = new byte[keyFrame.Length];
                    _onLightMax = keyFrame.Length - 1;

                    int j = 0;
                    for (int i = 0; i < _source.Length; i++)
                    {
                        _current[i] = i <= _onLightCount ? (byte)255 : (byte)0;
                        _source[i] = i <= _onLightCount ? (byte)255 : (byte)0;
                        j = (j + 1) % _source.Length;
                    }
                    j = 1;
                    for (int i = 0; i < _target.Length; i++)
                    {
                        _target[i] = _current[j];
                        j = (j + 1) % _current.Length;
                    }
                }

                for (int i = 0; i < keyFrame.Length; i++)
                {
                    keyFrame[i].Color = System.Drawing.Color.FromArgb(_current[i], keyFrame[i].Color);
                }

                return keyFrame;
            }
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
            Start();
            return new DeviceState(new PadState[0], new ButtonLightState[0]);
        }

        public override void Stop()
        {
            base.Stop();
        }

        public override void OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            
        }

        public override void RotaryTurned(RotaryDirection direction, byte rotaryNumber, byte? value)
        {
            if (rotaryNumber == 2)
            {
                lock (_memberLock)
                {
                    int fullOnLightCount = 0;
                    int partialOnLightCount = 0;
                    for (int i = _current.Length - 1; i >= 0; i--)
                    {
                        if (_current[i] == byte.MaxValue)
                        {
                            fullOnLightCount++;
                        }
                        else if (_current[i] != byte.MinValue)
                        {
                            partialOnLightCount++;
                        }
                    }
                    var existingOnLightCount = partialOnLightCount == 0 ? fullOnLightCount : fullOnLightCount + partialOnLightCount - 1;
                    if (existingOnLightCount == _onLightCount)
                    {
                        if (direction == RotaryDirection.Up)
                        {
                            _onLightCount -= 1;
                            if (_onLightCount < _onLightMin)
                            {
                                _onLightCount = _onLightMin;
                            }
                        }
                        else
                        {
                            _onLightCount += 1;
                            if (_onLightCount >= _onLightMax)
                            {
                                _onLightCount = _onLightMax;
                            }
                        }
                    }
                }
            }
            else if (rotaryNumber == 3)
            {
                for (int i = 0; i < 20; i++)
                {
                    Animate();
                }
            }
            else if (rotaryNumber == 4)
            {
                //Wave width
                //_waveDepth += .05d * (direction == RotaryDirection.Up ? 1 : -1);
                //if (_waveDepth < _waveDepthMin)
                //{
                //    _waveDepth = _waveDepthMin;
                //}
                //else if (_waveDepth > _waveDepthMax)
                //{
                //    _waveDepth = _waveDepthMax;
                //}
            }
        }

        public override void PadPressed(ButtonDirection direction, byte padNumber, int padCount)
        {
            
        }

        public override void OnBpmEvent(object sender)
        {
            Animate();
        }

        private void Animate()
        {
            lock (_memberLock)
            {
                if (_source == null || _target == null || _current == null)
                {
                    return;
                }

                bool atTarget = _smoothAlphaTransition.Transition(_source, _target, _current);

                if (atTarget)
                {
                    Array.Copy(_target, _source, _target.Length);
                    var first = _target[0];
                    for (int j = 0; j < _target.Length - 1; j++)
                    {
                        _target[j] = _target[j + 1];
                    }
                    _target[^1] = first;
                    int onCount = 0;
                    for (int j = 0; j < _target.Length; j++)
                    {
                        if (_target[j] == byte.MaxValue)
                        {
                            onCount++;
                        }
                    }
                    if (onCount < _onLightCount)
                    {
                        int lastOnIndex = -1;
                        for (int j = _target.Length - 1; j >= 0; j--)
                        {
                            if (_target[j] == byte.MaxValue)
                            {
                                lastOnIndex = j;
                            }
                            else if (_target[j] == byte.MinValue && lastOnIndex != -1)
                            {
                                _target[j] = byte.MaxValue;
                                break;
                            }
                            else if (j == 0)
                            {
                                _target[^1] = byte.MaxValue;
                            }
                        }
                    }
                    else if (onCount > _onLightCount)
                    {
                        int lastOnIndex = -1;
                        for (int j = _target.Length - 1; j >= 0; j--)
                        {
                            if (_target[j] == byte.MaxValue)
                            {
                                lastOnIndex = j;
                            }
                            else if (_target[j] == byte.MinValue && lastOnIndex != -1)
                            {
                                _target[lastOnIndex] = byte.MinValue;
                                break;
                            }
                            else if (j == 0)
                            {
                                _target[0] = byte.MinValue;
                            }
                        }
                    }
                    _smoothAlphaTransition.Transition(_source, _target, _current, recomputeDistance: true);
                }
            }
        }
    }
}
