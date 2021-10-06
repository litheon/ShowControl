using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

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
        private bool _isActive;
        private Timer _automationTimer;
        System.Drawing.Color[] _colors = new System.Drawing.Color[6]
        {
                System.Drawing.Color.FromArgb(0xFF, 0xFF, 0x00, 0x00),
                System.Drawing.Color.FromArgb(0xFF, 0xFF, 0x80, 0x00),
                System.Drawing.Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00),
                System.Drawing.Color.FromArgb(0xFF, 0x00, 0xFF, 0x00),
                System.Drawing.Color.FromArgb(0xFF, 0x00, 0x00, 0xFF),
                System.Drawing.Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)
        };

        public override bool IsActive
        {
            get
            {
                return _isActive;
            }
            set
            {
                _isActive = value;
            }
        }

        public override event EventHandler<DeviceOutputEventArgs> MidiAware_DeviceOutputMessage;

        public RainbowAnimation(int pixelCount)
        {
            _frameStart = 0;
            _rootFrame = new Pixel[pixelCount];
            _mode = RainbowAnimationMode.Automated;
            _automationTimer = new Timer(automaticModeFrameTick, null, 500, 500);

            int j = 0;
            for (int i = 0; i < _rootFrame.Length; i++)
            {
                _rootFrame[i] = new Pixel(_colors[j], 0, 0);
                j = (j + 1) % _colors.Length;
            }
        }

        private void automaticModeFrameTick(object state)
        {
            var existingFrameStart = Interlocked.CompareExchange(ref _frameStart, -1, -1);
            Interlocked.Exchange(ref _frameStart, (existingFrameStart + 1) % _rootFrame.Length);
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
            System.Drawing.Color[] colors = new System.Drawing.Color[count];
            for (int i = 0; i < count - 1; i++)
            {
                colors[i] = _colors[i % _colors.Length];
            }
            colors[count - 1] = System.Drawing.Color.FromArgb(0x00, 0x00, 0x00, 0x00);
            return colors;
        }

        public override void MidiAware_OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            var device = e.Source;
            if (e.EventType == DeviceInputEventType.RotaryEncoder)
            {
                SetMode(RainbowAnimationMode.Manual);
                if (e.RotaryDirection == RotaryDirection.Up)
                {
                    var existingFrameStart = Interlocked.CompareExchange(ref _frameStart, -1, -1);
                    Interlocked.Exchange(ref _frameStart, (existingFrameStart + 1) % _rootFrame.Length);
                }
                else if (e.RotaryDirection == RotaryDirection.Down)
                {
                    int newFrameStart = Interlocked.CompareExchange(ref _frameStart, -1, -1);
                    newFrameStart = newFrameStart - 1;
                    if (newFrameStart < 0)
                    {
                        newFrameStart = _rootFrame.Length - 1;
                    }
                    Interlocked.Exchange(ref _frameStart, newFrameStart);
                }
                var frameStart = Interlocked.CompareExchange(ref _frameStart, -1, -1);
                _frame = _rootFrame.Skip(frameStart).Concat(_rootFrame.Take(frameStart)).ToArray();
            }
            else if (e.EventType == DeviceInputEventType.Pad)
            {
                if (e.ButtonDirection == ButtonDirection.Press)
                {
                    SetMode(RainbowAnimationMode.Manual);
                    device.SetPadColor(e.PadNumber.Value, System.Drawing.Color.White);
                    var color = e.PadNumber.Value == e.Source.PadCount - 1 ? System.Drawing.Color.FromArgb(0x00, 0x00, 0x00, 0x00) : _colors[e.PadNumber.Value % _colors.Length];
                    Pixel[] colors = new Pixel[_rootFrame.Length];
                    for (int i = 0; i < colors.Length; i++)
                    {
                        colors[i] = new Pixel(color, 0x00, 0x00);
                    }
                    SetFrame(colors);
                }
                else if (e.ButtonDirection == ButtonDirection.Release)
                {
                    var color = e.PadNumber.Value == e.Source.PadCount - 1 ? System.Drawing.Color.FromArgb(0x00, 0x00, 0x00, 0x00) : _colors[e.PadNumber.Value % _colors.Length];
                    device.SetPadColor(e.PadNumber.Value, color);
                }
            }
            else if (e.EventType == DeviceInputEventType.Init)
            {
                var padCount = e.Source.PadCount;
                System.Drawing.Color[] padColors = null;
                if (e.Source.Capabilities.HasFlag(DeviceCapabilities.Pads))
                {
                    padColors = GetPadColors(padCount);
                    MidiAware_DeviceOutputMessage?.Invoke(this, new DeviceOutputEventArgs(padColors));
                }
                ButtonLightState[] buttonLightStates = null;
                if (e.Source.Capabilities.HasFlag(DeviceCapabilities.ButtonIllumination))
                {
                    buttonLightStates = new ButtonLightState[2]
                    {
                        new ButtonLightState(Button.Up, LightState.On),
                        new ButtonLightState(Button.Play, LightState.On)
                    };
                    MidiAware_DeviceOutputMessage?.Invoke(this, new DeviceOutputEventArgs(buttonLightStates));
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

        public override Pixel[] GetNextFrame()
        {
            if (_mode == RainbowAnimationMode.Automated)
            {
                var frameStart = Interlocked.CompareExchange(ref _frameStart, -1, -1);
                _frame = _rootFrame.Skip(frameStart).Concat(_rootFrame.Take(frameStart)).ToArray();
            }
            return _frame;
        }
    }
}
