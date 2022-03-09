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
    public class RainbowAnimation : MidiAwareAnimation
    {
        private int _frameStart;
        private readonly Pixel[] _rootFrame;
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

        private readonly Color[] _colors = new Color[6]
        {
                Color.FromArgb(0xFF, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xFF, 0xFF, 0x80, 0x00),
                Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00),
                Color.FromArgb(0xFF, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xFF, 0x00, 0x00, 0xFF),
                Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)
        };

        public RainbowAnimation(int pixelCount, IAudioService audioService)
        {
            _frameStart = 0;
            _rootFrame = new Pixel[pixelCount];
            _mode = AnimationMode.Automatic;
            _automationTimer = new Timer(AutomaticModeFrameTick, null, Timeout.Infinite, Timeout.Infinite);
            _navigationColorTimer = new Timer(NavigationTimerTick, null, 1000, 1000);
            _audioService = audioService;
            _pressedPads = new List<int>();

            int j = 0;
            for (int i = 0; i < _rootFrame.Length; i++)
            {
                _rootFrame[i] = new Pixel(_colors[j], 0, 0);
                j = (j + 1) % _colors.Length;
            }
        }

        ~RainbowAnimation()
        {
            _navigationColorTimer.Dispose();
            _automationTimer.Dispose();
        }

        private void AutomaticModeFrameTick(object state)
        {
            _frameStart.InterlockedIncrement(0, _rootFrame.Length - 1);
        }

        private void NavigationTimerTick(object state)
        {
            navigationColorIndex = (navigationColorIndex + 1) % _colors.Length;
            SetNavigationColor(_colors[navigationColorIndex]);
        }

        private void StartTimers()
        {
            _automationTimer.Change(500, 500);
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
                    _automationTimer.Change(500, 500);
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

        private Color[] GetPadColors(int count)
        {
            var padColors = new Color[count];
            for (int i = 0; i < padColors.Length - 2; i++)
            {
                padColors[i] = _colors[i % _colors.Length];
            }
            padColors[^2] = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
            padColors[^1] = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
            return padColors;
        }

        private void AudioService_OnFftCalculated(object sender, AudioFrameProcessedEventArgs e)
        {
            //add a gradient mode that animates from red->orange->yellow->green->blue, and adds colors between two key colors
            //if there are enough lights to do so
            //support a continous animation from one color to the next color
            //add a mode that grabs a window into a large continous gradient that music/the user/a timer will automatically scroll through
            if (_mode == AnimationMode.SoundReactive)
            {
                var spectrum = e.AudioSpectrum;

                var one = e.AudioSpectrum[1].Y;
                var two = e.AudioSpectrum[2].Y;
                var three = e.AudioSpectrum[3].Y;

                var current = (one + two + three) / 3;

                if (current > _lastFft * 1.2 && current > -25 && _frameCoolDown <= 0)
                {
                    _frameStart.InterlockedIncrement(0, _rootFrame.Length - 1);
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
                _lastFft = current;
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
            if (rotaryNumber == 1)
            {
                SetMode(AnimationMode.Manual);
                if (direction == RotaryDirection.Up)
                {
                    _frameStart.InterlockedDecrement(0, _rootFrame.Length - 1);
                }
                else if (direction == RotaryDirection.Down)
                {
                    _frameStart.InterlockedIncrement(0, _rootFrame.Length - 1);
                }
                var frameStart = _frameStart.InterlockedGet();
                _frame = _rootFrame.Skip(frameStart).Concat(_rootFrame.Take(frameStart)).ToArray();
            }
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
            _audioService.AudioFrameProcessed += AudioService_OnFftCalculated;

            Color[] padColors;
            if (inputDevice.Capabilities.HasFlag(DeviceCapabilities.Pads))
            {
                padColors = GetPadColors(inputDevice.PadCount);
                AssignPadColors(padColors);
            }
            else
            {
                padColors = new Color[0];
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
            return new DeviceState(padColors, buttonLightStates);
        }

        public override void Stop()
        {
            _pressedPads.Clear();
            _audioService.AudioFrameProcessed -= AudioService_OnFftCalculated;
            StopTimers();
        }

        public override Pixel[] GetNextFrame()
        {
            if (_mode == AnimationMode.SoundReactive || _mode == AnimationMode.Automatic)
            {
                var frameStart = _frameStart.InterlockedGet();
                _frame = _rootFrame.Skip(frameStart).Concat(_rootFrame.Take(frameStart)).ToArray();
            }
            return _frame;
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
