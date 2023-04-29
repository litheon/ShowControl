using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ShowControl.Utilities;
using ShowControl.Devices.Audio;
using System.Drawing;

namespace ShowControl.Animation
{
    public class RainbowAnimation : MidiAwareAnimation, IKeyAnimation
    {
        private int _frameStart;
        private readonly Pixel[] _rootFrame;
        private int[] _lastFrameDeltas;
        private readonly int[] _targetColorIndices;
        private readonly object _frameLock = new object();
        private Pixel[] _frame;
        private readonly object _modeLock = new object();
        private AnimationMode _lastMode;
        private AnimationMode _mode;
        private readonly Timer _automationTimer;
#pragma warning disable IDE0052 // Remove unread private members
        private readonly Timer _navigationColorTimer;
#pragma warning restore IDE0052 // Remove unread private members
        int navigationColorIndex = 0;

        private readonly IAudioService _audioService;
        private double _lastFft = double.NegativeInfinity;
        private const int DefaultFrameCooldown = AudioService.SampleRate / AudioService.FftLength / 6;
        private int _frameCoolDown = 1;
        private readonly List<int> _pressedPads;
        private long _lastRotaryTurnedTicks = 0;
        private readonly TimeSpan _minRotaryTurnTimeSpan = TimeSpan.FromMilliseconds(50);

        private readonly Color[] _colors = new Color[6]
        {
                Color.FromArgb(0xFF, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xFF, 0xFF, 0xA5, 0x00),
                Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00),
                Color.FromArgb(0xFF, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xFF, 0x00, 0x00, 0xFF),
                Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)
                //Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
        };

        public RainbowAnimation(int pixelCount, IAudioService audioService)
        {
            _frameStart = 0;
            _rootFrame = new Pixel[pixelCount];
            _lastFrameDeltas = new int[pixelCount];
            _mode = AnimationMode.Automatic;
            _automationTimer = new Timer(AutomaticModeFrameTick, null, Timeout.Infinite, Timeout.Infinite);
            _navigationColorTimer = new Timer(NavigationTimerTick, null, 1000, 1000);
            _audioService = audioService;
            _pressedPads = new List<int>();
            _targetColorIndices = new int[Math.Max(pixelCount, _colors.Length)];
            int j = 0;
            for (int i = 0; i < _rootFrame.Length; i++)
            {
                _rootFrame[i] = new Pixel(_colors[j], 0, 0);
                j = (j + 1) % _colors.Length;
            }
            j = 1;
            for (int i = 0; i < _targetColorIndices.Length; i++)
            {
                _targetColorIndices[i] = j;
                j = (j + 1) % _colors.Length;
            }
            Array.Fill(_lastFrameDeltas, 0);
        }

        ~RainbowAnimation()
        {
            _navigationColorTimer.Dispose();
            _automationTimer.Dispose();
        }

        private void AutomaticModeFrameTick(object state)
        {
            Animate();
            //_frameStart.InterlockedIncrement(0, _rootFrame.Length - 1);
        }

        private void Animate(bool forward = true)
        {
            lock (_rootFrame)
            {
                int minDistance = 256;
                int maxDistance = 0x00;
            
                for (int i = 0; i < _rootFrame.Length; i++)
                {
                    var targetColorIndex = _targetColorIndices[i];
                    if (!forward)
                    {
                        if (i == 0)
                        {
                            targetColorIndex = _targetColorIndices[^1];
                        }
                        else
                        {
                            targetColorIndex = _targetColorIndices[i - 1];
                        }
                    }

                    var rDistance = Math.Abs(_colors[targetColorIndex].R - _rootFrame[i].Color.R);
                    var gDistance = Math.Abs(_colors[targetColorIndex].G - _rootFrame[i].Color.G);
                    var bDistance = Math.Abs(_colors[targetColorIndex].B - _rootFrame[i].Color.B);
                    if (rDistance > 0)
                    {
                        minDistance = Math.Min(minDistance, rDistance);
                    }
                    if (gDistance > 0)
                    {
                        minDistance = Math.Min(minDistance, gDistance);
                    }
                    if (bDistance > 0)
                    {
                        minDistance = Math.Min(minDistance, bDistance);
                    }
                    maxDistance = Math.Max(maxDistance, rDistance);
                    maxDistance = Math.Max(maxDistance, gDistance);
                    maxDistance = Math.Max(maxDistance, bDistance);
                }
                if (maxDistance == 0)
                {
                    if (forward)
                    {
                        var first = _targetColorIndices[0];
                        for (int i = 0; i < _targetColorIndices.Length - 1; i++)
                        {
                            _targetColorIndices[i] = _targetColorIndices[i + 1];
                        }
                        _targetColorIndices[^1] = first;
                    }
                    else
                    {
                        var last = _targetColorIndices[^1];
                        for (int i = _targetColorIndices.Length - 1; i > 0; i--)
                        {
                            _targetColorIndices[i] = _targetColorIndices[i - 1];
                        }
                        _targetColorIndices[0] = last;
                    }

                    Array.Fill(_lastFrameDeltas, 0);
                    return;
                }
                for (int i = 0; i < _rootFrame.Length; i++)
                {
                    var targetColorIndex = _targetColorIndices[i];
                    if (!forward)
                    {
                        if (i == 0)
                        {
                            targetColorIndex = _targetColorIndices[^1];
                        }
                        else
                        {
                            targetColorIndex = _targetColorIndices[i - 1];
                        }
                    }

                    var rDistance = _colors[targetColorIndex].R - _rootFrame[i].Color.R;
                    var gDistance = _colors[targetColorIndex].G - _rootFrame[i].Color.G;
                    var bDistance = _colors[targetColorIndex].B - _rootFrame[i].Color.B;

                    byte R = HandleFunction(_rootFrame[i].Color.R, rDistance);
                    byte G = HandleFunction(_rootFrame[i].Color.G, gDistance);
                    byte B = HandleFunction(_rootFrame[i].Color.B, bDistance);

                    _rootFrame[i].Color = Color.FromArgb(0xFF, R, G, B);

                    byte HandleFunction(byte value, int distance)
                    {
                        if (distance == 0)
                        {
                            return value;
                        }

                        bool subtract = distance < 0;
                        var lastRate = Math.Abs((_lastFrameDeltas[i] > 0 ? _lastFrameDeltas[i] / distance : 1));
                        var closeRate = minDistance < 256 ? Math.Abs(((double)1 / minDistance)) : 256;

                        int delta = 0;
                        //if (closeRate / lastRate >= 1.5 && Math.Abs(distance) > 1 && _lastFrameDeltas[i] > 0)
                        //{
                        //    delta = subtract ? -2 : 2;
                        //}
                        //else
                        //{
                            delta = subtract ? -1 : 1;
                        //}

                        _lastFrameDeltas[i] = delta;
                        return (byte)(value + delta);
                    }
                }
            }
        }


        private void NavigationTimerTick(object state)
        {
            navigationColorIndex = (navigationColorIndex + 1) % _colors.Length;
            SetNavigationColor(_colors[navigationColorIndex]);
        }

        private void StartTimers()
        {
            //_automationTimer.Change(500, 500);
            _automationTimer.Change(1, 1);
        }

        private void StopTimers()
        {
            _automationTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void SetMode(AnimationMode mode)
        {
            lock(_modeLock)
            {
                if (_mode != mode)
                {
                    _lastMode = _mode;
                }
                _mode = mode;
                if (_mode == AnimationMode.Automatic)
                {
                    //_automationTimer.Change(500, 500);
                    _automationTimer.Change(1, 1);
                }
                else
                {
                    _automationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        private void RestoreMode()
        {
            lock(_modeLock)
            {
                SetMode(_lastMode);
            }
        }

        private void SetFrame(Pixel[] frame)
        {
            lock(_frameLock)
            {
                _frame = frame;
            }
        }

        private PadState[] GetPadStates(int count)
        {
            var padStates = new PadState[count];
            for (int i = 0; i < padStates.Length - 2; i++)
            {
                padStates[i] = new PadState(PadIllumination.Solid, _colors[i % _colors.Length]);
            }
            padStates[^2].Color = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
            padStates[^2] = new PadState(PadIllumination.Solid, Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
            padStates[^1] = new PadState(PadIllumination.Solid, Color.FromArgb(0x00, 0x00, 0x00, 0x00));
            return padStates;
        }

        private void AudioService_OnFftCalculated(object sender, AudioFrameProcessedEventArgs e)
        {
            //add a gradient mode that animates from red->orange->yellow->green->blue, and adds colors between two key colors
            //if there are enough lights to do so
            //support a continous animation from one color to the next color
            //add a mode that grabs a window into a large continous gradient that music/the user/a timer will automatically scroll through
            if (_mode == AnimationMode.SoundReactive)
            {
                if (e.CurrentLevel >= -50.0d)
                {
                    var level = Convert.ToInt32(Math.Min(Math.Max(Math.Round(e.CurrentLevel / e.AverageLevel) * 10, 1), 10));
                    for (int i = 0; i < 10; i++)
                    {
                        Animate();
                    }
                    _frameCoolDown = DefaultFrameCooldown;
                }
                else if (_frameCoolDown >= 1)
                {
                    _frameCoolDown--;
                }
                else if (_frameCoolDown == 0)
                {
                    _frameCoolDown = -1;
                }
            }
        }

        public override void OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            if (e.EventType == DeviceInputEventType.Transport)
            {
                if (e.TransportButton == TransportButton.Play && e.ButtonDirection == ButtonDirection.Press)
                {
                    SetMode(AnimationMode.Automatic);
                }
            }
        }

        public override void PadPressed(ButtonDirection direction, byte padNumber, int padCount)
        {
            if (direction == ButtonDirection.Press)
            {
                if (padNumber == padCount - 1)
                {
                    SetMode(AnimationMode.Manual);
                    var color = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
                    SetFrameWithSingleColor(color);
                }
                else if (padNumber == padCount - 2)
                {
                    SetMode(AnimationMode.SoundReactive);
                }
                else
                {
                    SetMode(AnimationMode.Manual);
                    var color = _colors[padNumber % _colors.Length];
                    SetFrameWithSingleColor(color);
                    _pressedPads.Add(padNumber);
                }
            }
            else if (direction == ButtonDirection.Release)
            {
                if (padNumber < padCount - 2)
                {
                    _pressedPads.Remove(padNumber);
                    if (_pressedPads.Count == 0)
                    {
                        RestoreMode();
                    }
                    else
                    {

                        var recentColor = _colors[_pressedPads.Last() % _colors.Length];
                        SetFrameWithSingleColor(recentColor);
                    }
                }
            }
        }

        public override void RotaryTurned(RotaryDirection direction, byte rotaryNumber, byte? value)
        {
            //var currentTicks = DateTime.Now.Ticks;
            //if (currentTicks < _lastRotaryTurnedTicks + _minRotaryTurnTimeSpan.Ticks)
            //{
            //    return;
            //}
            //_lastRotaryTurnedTicks = currentTicks;
            if (rotaryNumber == 1 && direction != RotaryDirection.None)
            {
                for (int i = 0; i < 20; i++)
                {
                    Animate(direction == RotaryDirection.Up);
                }
                
                SetMode(AnimationMode.Manual);
                //if (direction == RotaryDirection.Up)
                //{
                //    _frameStart.InterlockedDecrement(0, _rootFrame.Length - 1);
                //}
                //else if (direction == RotaryDirection.Down)
                //{
                //    _frameStart.InterlockedIncrement(0, _rootFrame.Length - 1);
                //}
                //var frameStart = _frameStart.InterlockedGet();
                lock(_rootFrame)
                {
                    lock (_frame)
                    {
                        Array.Copy(_rootFrame, _frame, _rootFrame.Length);
                    }
                }
            }
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
            _audioService.AudioFrameProcessed += AudioService_OnFftCalculated;

            PadState[] padStates;
            if (inputDevice.Capabilities.HasFlag(DeviceCapabilities.Pads))
            {
                padStates = GetPadStates(inputDevice.PadCount);
                AssignPadStates(padStates);
            }
            else
            {
                padStates = new PadState[0];
            }
            ButtonLightState[] buttonLightStates;
            if (inputDevice.Capabilities.HasFlag(DeviceCapabilities.ButtonIllumination))
            {
                buttonLightStates = new ButtonLightState[2]
                {
                    new ButtonLightState(Button.Up, LightState.On),
                    new ButtonLightState(Button.Play, LightState.On)
                };
            }
            else
            {
                buttonLightStates = new ButtonLightState[0];
            }

            StartTimers();
            return new DeviceState(padStates, buttonLightStates);
        }

        public override void Stop()
        {
            _pressedPads.Clear();
            _audioService.AudioFrameProcessed -= AudioService_OnFftCalculated;
            StopTimers();
        }

        public Pixel[] GetNextFrame()
        {
            lock(_rootFrame)
            {
                if (_mode == AnimationMode.SoundReactive || _mode == AnimationMode.Automatic)
                {
                    var frameStart = _frameStart.InterlockedGet();
                    _frame = _rootFrame.Skip(frameStart).Concat(_rootFrame.Take(frameStart)).ToArray();
                }
                return _frame;
            }
        }

        private void SetFrameWithSingleColor(Color color)
        {
            Pixel[] colors = new Pixel[_rootFrame.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Pixel(color, 0x00, 0x00);
            }
            SetFrame(colors);
        }
    }
}
