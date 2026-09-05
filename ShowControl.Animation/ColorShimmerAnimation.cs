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
    public class ColorShimmerAnimation : MidiAwareAnimation, IKeyAnimation, IDisposable
    {
        private readonly Color[] _colors;

        private readonly Pixel[] _rootFrame;
        private readonly Color[] _currentAnimationColors;
        private readonly Color[] _targetAnimationColors;
        private readonly Color[] _sourceAnimationColors;
        private readonly Color[] _solidColorFrame;

        private readonly object _frameLock = new object();
        private readonly object _modeLock = new object();
        private AnimationMode _lastMode;
        private AnimationMode _mode;

        private readonly CancellationToken _cancellationToken;
        private readonly Timer _automationTimer;
        private readonly Thread _automationThread;
        private readonly AutoResetEvent _frameAnimateEvent = new AutoResetEvent(false);
#pragma warning disable IDE0052 // Remove unread private members
        private readonly Timer _navigationColorTimer;
#pragma warning restore IDE0052 // Remove unread private members
        int navigationColorIndex = 0;

        private readonly IAudioService _audioService;
        private const int DefaultFrameCooldown = 8;
        private int _frameCoolDown = 1;
        private const int ColorChannelDistanceMax = 4;
        private Random _colorDeltaRandom;
        private Random _directionRandom;

        private readonly List<int> _pressedPads;
        private readonly SmoothColorTransition _smoothColorTransition;

        bool _specialModesEnabled = true;

        bool _disposed = false;
        public ColorShimmerAnimation(
            int pixelCount,
            Color[] colors,
            IAudioService audioService,
            CancellationToken cancellationToken)
        {
            _rootFrame = new Pixel[pixelCount];

            _colors = ConvertColors(colors);

            _currentAnimationColors = new Color[pixelCount];
            _targetAnimationColors = new Color[Math.Max(pixelCount, _colors.Length)];
            _sourceAnimationColors = new Color[_targetAnimationColors.Length];
            _solidColorFrame = new Color[_targetAnimationColors.Length];

            
            _automationTimer = new Timer(AutomaticModeFrameTick, null, Timeout.Infinite, Timeout.Infinite);
            _automationThread = new Thread(AutomationThreadStart);
            _automationThread.Start();
            _lastMode = AnimationMode.Automatic;
            _mode = AnimationMode.Automatic;
            _navigationColorTimer = new Timer(NavigationTimerTick, null, 1000, 1000);

            _audioService = audioService;
            _pressedPads = new List<int>();

            _smoothColorTransition = new SmoothColorTransition();

            _colorDeltaRandom = new Random();
            _directionRandom = new Random();

            int j = 0;
            for (int i = 0; i < _rootFrame.Length; i++)
            {
                var newColor = Color.FromArgb(
                    _colors[0].A,
                    ShimmerColorChannel(_colors[0].R),
                    ShimmerColorChannel(_colors[0].G),
                    ShimmerColorChannel(_colors[0].B));
                _currentAnimationColors[i] = newColor;
                _sourceAnimationColors[i] = newColor;
                j = (j + 1) % _colors.Length;
            }
            j = 1;
            for (int i = 0; i < _targetAnimationColors.Length; i++)
            {
                var newColor = Color.FromArgb(
                    _colors[0].A,
                    ShimmerColorChannel(_colors[0].R),
                    ShimmerColorChannel(_colors[0].G),
                    ShimmerColorChannel(_colors[0].B));

                _targetAnimationColors[i] = newColor;
                j = (j + 1) % _colors.Length;
            }

            _cancellationToken = cancellationToken;
        }

        private byte ShimmerColorChannel(byte color)
        {
            var subtract = _directionRandom.Next(2) == 1;
            int val = color + (_colorDeltaRandom.Next(ColorChannelDistanceMax) * (subtract ? -1 : 1));
            if (val > byte.MaxValue)
            {
                val = byte.MaxValue;
            }
            else if (val < byte.MinValue)
            {
                val = byte.MinValue;
            }
            return Convert.ToByte(val);
        }

        ~ColorShimmerAnimation()
        {
            Dispose();
        }

        private void AutomaticModeFrameTick(object state)
        {
            _frameAnimateEvent.Set();
        }

        private void AutomationThreadStart(object state)
        {
            while(!_cancellationToken.IsCancellationRequested)
            {
                _frameAnimateEvent.WaitOne();
                _automationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                Animate(forward: true, fromInput: false);
                _automationTimer.Change(20, Timeout.Infinite);
            }
            _automationTimer.Dispose();
        }

        private void Animate(bool forward, bool fromInput)
        {
            if (_frameCoolDown > 0)
            {
                _frameCoolDown--;
                return;
            }
            else
            {
                _frameCoolDown = DefaultFrameCooldown;
            }

            AnimationMode mode;
            lock(_modeLock)
            {
                mode = _mode;
            }
            lock (_rootFrame)
            {
                for (int i = 0; i < _currentAnimationColors.Length; i++)
                {
                    var existingColor = _currentAnimationColors[i];
                    var targetColor = _targetAnimationColors[i];
                    var R = existingColor.R;
                    var G = existingColor.G;
                    var B = existingColor.B;
                    if (R < targetColor.R)
                    {
                        R++;
                    }
                    else if (B < targetColor.B)
                    {
                        B++;
                    }
                    else if (G < targetColor.G)
                    {
                        G++;
                    }
                    else if (R > targetColor.R)
                    {
                        R--;
                    }
                    else if (B > targetColor.B)
                    {
                        B--;
                    }
                    else if (G > targetColor.G)
                    {
                        G--;
                    }
                    else if (mode == AnimationMode.Automatic)
                    {
                        var newColor = Color.FromArgb(
                            _colors[0].A,
                            ShimmerColorChannel(_colors[0].R),
                            ShimmerColorChannel(_colors[0].G),
                            ShimmerColorChannel(_colors[0].B));
                        _targetAnimationColors[i] = newColor;
                    }

                    _currentAnimationColors[i] = Color.FromArgb(R, G, B);
                }

                SetFrame(_currentAnimationColors);
            }
        }

        private void NavigationTimerTick(object state)
        {
            navigationColorIndex = (navigationColorIndex + 1) % _colors.Length;
            SetNavigationColor(_colors[navigationColorIndex]);
        }

        private void StartTimers()
        {
            _automationTimer.Change(20, Timeout.Infinite);
            //_automationTimer.Change(1, 1);
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
                Array.Copy(_currentAnimationColors, _sourceAnimationColors, _currentAnimationColors.Length);
                _smoothColorTransition.Transition(_sourceAnimationColors, _targetAnimationColors, _currentAnimationColors, recomputeDistance: true);
                if (_mode == AnimationMode.Automatic || _mode == AnimationMode.Manual)
                {
                    _automationTimer.Change(20, Timeout.Infinite);
                }
                else if (_mode == AnimationMode.SoundReactive)
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

        private void SetFrame(Color[] frame)
        {
            lock(_frameLock)
            {
                for (int i = 0; i < _rootFrame.Length; i++)
                {
                    _rootFrame[i].Color = frame[i];
                }
            }
        }

        private PadState[] GetPadStates(int count)
        {
            var padStates = new PadState[count];
            var endLength = _specialModesEnabled ? padStates.Length - 2 : padStates.Length;
            for (int i = 0; i < endLength; i++)
            {
                padStates[i] = new PadState(PadIllumination.Solid, _colors[i % _colors.Length]);
            }
            if (_specialModesEnabled)
            {
                padStates[^2].Color = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
                padStates[^2] = new PadState(PadIllumination.Solid, Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
                padStates[^1] = new PadState(PadIllumination.Solid, Color.FromArgb(0x00, 0x00, 0x00, 0x00));
            }
            return padStates;
        }

        private void AudioService_OnFftCalculated(object sender, AudioFrameProcessedEventArgs e)
        {
            //add a gradient mode that animates from red->orange->yellow->green->blue, and adds colors between two key colors
            //if there are enough lights to do so
            //support a continous animation from one color to the next color
            //add a mode that grabs a window into a large continous gradient that music/the user/a timer will automatically scroll through
            // The dispatch thread owns the event args' disposal; subscribers read, never dispose.
            {
                if (_mode == AnimationMode.SoundReactive)
                {
                    if (e.CurrentLevel >= -30.0d && _frameCoolDown <= 0)
                    {
                        var level = Convert.ToInt32(Math.Min(Math.Max(Math.Round(e.CurrentLevel / e.AverageLevel) * 5, 1), 5));
                        for (int i = 0; i < level; i++)
                        {
                            Animate(forward: true, fromInput: true);
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
                if (padNumber == padCount - 1 && _specialModesEnabled)
                {
                    SetMode(AnimationMode.Manual);
                    var color = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
                    SetTargetFrameToSingleColor(color);
                    _pressedPads.Add(padNumber);
                }
                else if (padNumber == padCount - 2 && _specialModesEnabled)
                {
                    SetMode(AnimationMode.SoundReactive);
                }
                else
                {
                    SetMode(AnimationMode.Manual);
                    var color = _colors[padNumber % _colors.Length];
                    SetTargetFrameToSingleColor(color);
                    _pressedPads.Add(padNumber);
                }
            }
            else if (direction == ButtonDirection.Release)
            {
                if (_pressedPads.Count == 0)
                {
                    return;
                }

                if (padNumber != padCount - 2 || !_specialModesEnabled)
                {
                    _pressedPads.Remove(padNumber);
                    if (_pressedPads.Count == 0)
                    {
                        RestoreMode();
                    }
                    else
                    {

                        var recentColor = _colors[_pressedPads.Last() % _colors.Length];
                        SetTargetFrameToSingleColor(recentColor);
                    }
                }
            }
        }

        public override void RotaryTurned(RotaryDirection direction, byte rotaryNumber, byte? value)
        {
            if (rotaryNumber == 1 && direction != RotaryDirection.None)
            {
                for (int i = 0; i < 10; i++)
                {
                    Animate(direction == RotaryDirection.Up, fromInput: true);
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

            SetMode(AnimationMode.Automatic);
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
            lock (_rootFrame)
            {
                Pixel[] frame = new Pixel[_rootFrame.Length];
                Array.Copy(_rootFrame, frame, _rootFrame.Length);
                return frame;
            }
        }

        private void SetTargetFrameToSingleColor(Color color)
        {
            lock (_rootFrame)
            {
                Array.Copy(_currentAnimationColors, _sourceAnimationColors, _currentAnimationColors.Length);
                for (int i = 0; i < _solidColorFrame.Length; i++)
                {
                    _solidColorFrame[i] = color;
                }
                _smoothColorTransition.Transition(_sourceAnimationColors, _solidColorFrame, _currentAnimationColors, recomputeDistance: true);
            }
        }

        private System.Drawing.Color[] ConvertColors(System.Drawing.Color[] colors)
        {
            Color[] converted = new Color[colors.Length];
            for (int i = 0; i < converted.Length; i++)
            {
                var color = colors[i];
                if (color.R > 0 && (color.G < 4 || color.B < 2))
                {
                    converted[i] = Color.FromArgb(0xFF, color.R, Math.Max((byte)0x04, color.G), color.B);
                }
                else if (color.B > 0 && (color.R < 6 || color.G < 3))
                {
                    converted[i] = Color.FromArgb(0xFF, Math.Max((byte)0x06, color.R), Math.Max((byte)0x03, color.G), color.B);
                }
                else if (color.G > 0 && (color.R < 2 || color.B < 6))
                {
                    converted[i] = Color.FromArgb(0xFF, Math.Max((byte)0x02, color.R), color.G, Math.Max((byte)0x06, color.B));
                }
                else
                {
                    converted[i] = color;
                }
            }
            return converted;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                GC.SuppressFinalize(this);
                Stop();
                _frameAnimateEvent.Set();
                _navigationColorTimer.Dispose();
                _disposed = true;
            }
        }
    }
}
