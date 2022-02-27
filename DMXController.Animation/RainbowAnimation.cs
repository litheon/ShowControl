using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ShowControl.Utilities;
using ShowControl.Devices.Audio;

namespace ShowControl.Animation
{
    public class RainbowAnimation : MidiAwareAnimation
    {
        private enum RainbowAnimationMode { Manual, StaticColor, Automated };
        private int _frameStart;
        private Pixel[] _rootFrame;
        private object _frameLock = new object();
        private Pixel[] _frame;
        private object _modeLock = new object();
        private RainbowAnimationMode _mode;
        private Timer _automationTimer;
        private Timer _navigationColorTimer;
        private System.Drawing.Color[] _padColors;
        int navigationColorIndex = 0;

        private IAudioService _audioService;
        private double _lastFft = double.NegativeInfinity;
        private const int defaultFrameCooldown = AudioService.SampleRate / AudioService.FftLength / 6;
        private int frameCoolDown = 1;

        System.Drawing.Color[] _colors = new System.Drawing.Color[6]
        {
                System.Drawing.Color.FromArgb(0xFF, 0xFF, 0x00, 0x00),
                System.Drawing.Color.FromArgb(0xFF, 0xFF, 0x80, 0x00),
                System.Drawing.Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00),
                System.Drawing.Color.FromArgb(0xFF, 0x00, 0xFF, 0x00),
                System.Drawing.Color.FromArgb(0xFF, 0x00, 0x00, 0xFF),
                System.Drawing.Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)
        };

        public RainbowAnimation(int pixelCount, IAudioService audioService)
        {
            _frameStart = 0;
            _rootFrame = new Pixel[pixelCount];
            _mode = RainbowAnimationMode.Automated;
            _automationTimer = new Timer(automaticModeFrameTick, null, Timeout.Infinite, Timeout.Infinite);
            _navigationColorTimer = new Timer(navigationTimerTick, null, 1000, 1000);
            _audioService = audioService;

            int j = 0;
            for (int i = 0; i < _rootFrame.Length; i++)
            {
                _rootFrame[i] = new Pixel(_colors[j], 0, 0);
                j = (j + 1) % _colors.Length;
            }
        }

        private void automaticModeFrameTick(object state)
        {
            _frameStart.InterlockedIncrement(0, _rootFrame.Length - 1);
        }

        private void navigationTimerTick(object state)
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

        private void SetMode(RainbowAnimationMode mode)
        {
            lock(_modeLock)
            {
                _mode = mode;
                if (_mode == RainbowAnimationMode.Automated)
                {
                    _automationTimer.Change(500, 500);
                }
                else
                {
                    _automationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        private void SetFrame(Pixel[] frame)
        {
            lock(_frameLock)
            {
                _frame = frame;
            }
        }

        private System.Drawing.Color[] GetPadColors(int count)
        {
            return _padColors;
        }

        private void _audioService_OnFftCalculated(object sender, AudioFrameProcessedEventArgs e)
        {
            var spectrum = e.AudioSpectrum;

            var one = e.AudioSpectrum[1].Y;
            var two = e.AudioSpectrum[2].Y;
            var three = e.AudioSpectrum[3].Y;

            var current = (one + two + three) / 3;

            if (current > _lastFft * 1.2 && current > -25 && frameCoolDown <= 0)
            {
                _frameStart.InterlockedIncrement(0, _rootFrame.Length - 1);
                var frameStart = _frameStart.InterlockedGet();
                _frame = _rootFrame.Skip(frameStart).Concat(_rootFrame.Take(frameStart)).ToArray();
                frameCoolDown = defaultFrameCooldown;
            }
            else if (frameCoolDown >= 1)
            {
                frameCoolDown--;
            }
            else if (frameCoolDown == 0)
            {
                frameCoolDown = -1;
            }
            _lastFft = current;
        }

        public override void MidiAware_OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            var device = e.Source;
            if (e.EventType == DeviceInputEventType.RotaryEncoder)
            {
                SetMode(RainbowAnimationMode.Manual);
                if (e.RotaryNumber == 1)
                {
                    if (e.RotaryDirection == RotaryDirection.Up)
                    {
                        _frameStart.InterlockedIncrement(0, _rootFrame.Length - 1);
                    }
                    else if (e.RotaryDirection == RotaryDirection.Down)
                    {
                        _frameStart.InterlockedDecrement(0, _rootFrame.Length - 1);
                    }
                }

                var frameStart = _frameStart.InterlockedGet();
                _frame = _rootFrame.Skip(frameStart).Concat(_rootFrame.Take(frameStart)).ToArray();
            }
            else if (e.EventType == DeviceInputEventType.Pad)
            {
                if (e.ButtonDirection == ButtonDirection.Press)
                {
                    SetMode(RainbowAnimationMode.Manual);
                    Pixel[] colors = new Pixel[_rootFrame.Length];
                    if (e.PadNumber.Value == e.Source.PadCount - 1)
                    {
                        SetPadColor(e.PadNumber.Value, System.Drawing.Color.Silver);
                        var color = System.Drawing.Color.FromArgb(0x00, 0x00, 0x00, 0x00);
                        for (int i = 0; i < colors.Length; i++)
                        {
                            colors[i] = new Pixel(color, 0x00, 0x00);
                        }
                    }
                    else if (e.PadNumber.Value == e.Source.PadCount - 2)
                    {
                        SetPadColor(e.PadNumber.Value, System.Drawing.Color.White);
                        for (int i = 0; i < colors.Length; i++)
                        {
                            colors[i] = new Pixel(System.Drawing.Color.FromArgb(0xFF, 0x00, 0x00, 0x00), 0x00, 0xFF);
                        }
                    }
                    else
                    {
                        SetPadColor(e.PadNumber.Value, System.Drawing.Color.White);
                        var color = _colors[e.PadNumber.Value % _colors.Length];
                        for (int i = 0; i < colors.Length; i++)
                        {
                            colors[i] = new Pixel(color, 0x00, 0x00);
                        }
                    }
                    SetFrame(colors);
                }
                else if (e.ButtonDirection == ButtonDirection.Release)
                {
                    var color = _padColors[e.PadNumber.Value];
                    SetPadColor(e.PadNumber.Value, color);
                }
            }
            else if (e.EventType == DeviceInputEventType.Transport)
            {
                if (e.TransportButton == TransportButton.Play && e.ButtonDirection == ButtonDirection.Press)
                {
                    SetMode(RainbowAnimationMode.Automated);
                }
            }
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
            _audioService.AudioFrameProcessed += _audioService_OnFftCalculated;
            _padColors = new System.Drawing.Color[inputDevice.PadCount];

            for (int i = 0; i < _padColors.Length - 2; i++)
            {
                _padColors[i] = _colors[i % _colors.Length];
            }
            _padColors[_padColors.Length - 2] = System.Drawing.Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
            _padColors[_padColors.Length - 1] = System.Drawing.Color.FromArgb(0x00, 0x00, 0x00, 0x00);

            System.Drawing.Color[] padColors = new System.Drawing.Color[0];
            if (inputDevice.Capabilities.HasFlag(DeviceCapabilities.Pads))
            {
                padColors = GetPadColors(inputDevice.PadCount);
            }
            ButtonLightState[] buttonLightStates = new ButtonLightState[0];
            if (inputDevice.Capabilities.HasFlag(DeviceCapabilities.ButtonIllumination))
            {
                buttonLightStates = new ButtonLightState[2]
                {
                    new ButtonLightState(Button.Up, LightState.On),
                    new ButtonLightState(Button.Play, LightState.On)
                };
            }

            StartTimers();
            return new DeviceState(padColors, buttonLightStates);
        }

        public override void Stop()
        {
            _audioService.AudioFrameProcessed -= _audioService_OnFftCalculated;
            StopTimers();
        }

        public override Pixel[] GetNextFrame()
        {
            if (_mode == RainbowAnimationMode.Automated)
            {
                var frameStart = _frameStart.InterlockedGet();
                _frame = _rootFrame.Skip(frameStart).Concat(_rootFrame.Take(frameStart)).ToArray();
            }
            return _frame;
        }
    }
}
