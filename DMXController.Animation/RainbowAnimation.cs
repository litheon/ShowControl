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
    public class RainbowAnimation : MidiAwareAnimation, IKeyAnimation, IModeEnabledAnimation, IDisposable
    {
        private readonly Color[] _colors;

        private readonly Pixel[] _rootFrame;
        private readonly Color[] _currentAnimationColors;
        private readonly Color[] _targetAnimationColors;
        private readonly Color[] _sourceAnimationColors;
        private readonly Color[] _solidColorFrame;

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
        private const int DefaultFrameCooldown = 6;
        private int _frameCoolDown = 1;
        private double _beats = 0;
        private double _lastAmplitudeRatio = 0;

        private readonly List<int> _pressedPads;
        private readonly SmoothColorTransition _solidColorTransition;
        private readonly SmoothColorTransition _variableColorTransition;

        bool _specialModesEnabled = true;

        bool _padPressedReachedTarget = false;

        bool _disposed = false;
        public RainbowAnimation(
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
            _solidColorFrame = new Color[pixelCount];

            
            _automationTimer = new Timer(AutomaticModeFrameTick, null, Timeout.Infinite, Timeout.Infinite);
            _automationThread = new Thread(AutomationThreadStart);
            _automationThread.Start();
            _lastMode = AnimationMode.Automatic;
            _mode = AnimationMode.Automatic;
            _navigationColorTimer = new Timer(NavigationTimerTick, null, 1000, 1000);

            _audioService = audioService;
            _pressedPads = new List<int>();

            _variableColorTransition = new SmoothColorTransition();
            _solidColorTransition = new SmoothColorTransition();

            int j = 0;
            for (int i = 0; i < _rootFrame.Length; i++)
            {
                _currentAnimationColors[i] = _colors[j];
                _sourceAnimationColors[i] = _colors[j];
                j = (j + 1) % _colors.Length;
            }
            j = 1;
            for (int i = 0; i < _targetAnimationColors.Length; i++)
            {
                _targetAnimationColors[i] = _colors[j];
                j = (j + 1) % _colors.Length;
            }

            _cancellationToken = cancellationToken;

            SetMode(AnimationMode.Automatic);
        }

        ~RainbowAnimation()
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
            AnimationMode mode;
            lock(_modeLock)
            {
                mode = _mode;
                lock (_rootFrame)
                {
                    if (_mode == AnimationMode.Manual)
                    {
                        int iterations = _padPressedReachedTarget ? 1 : 30;
                        for (int i = 0; i < iterations; i++)
                        {
                            // Should target be _targetAnimationColors?
                            if (_solidColorTransition.Transition(_sourceAnimationColors, _solidColorFrame, _currentAnimationColors) && fromInput)
                            {
                                _padPressedReachedTarget = true;
                                Array.Copy(_solidColorFrame, _sourceAnimationColors, _solidColorFrame.Length);

                                if (forward)
                                {
                                    var first = _solidColorFrame[0];
                                    for (int j = 0; j < _solidColorFrame.Length - 1; j++)
                                    {
                                        _solidColorFrame[j] = _solidColorFrame[j + 1];
                                    }
                                    _solidColorFrame[^1] = first;
                                }
                                else
                                {
                                    var last = _solidColorFrame[^1];
                                    for (int j = _solidColorFrame.Length - 1; j > 0; j--)
                                    {
                                        _solidColorFrame[j] = _solidColorFrame[j - 1];
                                    }
                                    _solidColorFrame[0] = last;
                                }
                            }
                        }
                    }
                    else
                    {
                        bool atTarget = _variableColorTransition.Transition(_sourceAnimationColors, _targetAnimationColors, _currentAnimationColors);

                        if (atTarget)
                        {
                            Array.Copy(_targetAnimationColors, _sourceAnimationColors, _targetAnimationColors.Length);
                            if (forward)
                            {
                                var first = _targetAnimationColors[0];
                                for (int i = 0; i < _targetAnimationColors.Length - 1; i++)
                                {
                                    _targetAnimationColors[i] = _targetAnimationColors[i + 1];
                                }
                                _targetAnimationColors[^1] = first;
                            }
                            else
                            {
                                var last = _targetAnimationColors[^1];
                                for (int i = _targetAnimationColors.Length - 1; i > 0; i--)
                                {
                                    _targetAnimationColors[i] = _targetAnimationColors[i - 1];
                                }
                                _targetAnimationColors[0] = last;
                            }
                        }
                    }

                    SetFrame(_currentAnimationColors);
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
            _automationTimer.Change(20, Timeout.Infinite);
            //_automationTimer.Change(1, 1);
        }

        private void StopTimers()
        {
            _automationTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void SetMode(AnimationMode mode)
        {
            lock(_modeLock)
            {
                System.Diagnostics.Debug.WriteLine($"Set mode to {mode.ToString()}");
                if (_mode != mode)
                {
                    _lastMode = _mode;
                }
                _mode = mode;

                lock (_rootFrame)
                {
                    System.Diagnostics.Debug.WriteLine($"Set current animation colors");
                    Array.Copy(_currentAnimationColors, _sourceAnimationColors, _currentAnimationColors.Length);
                    if (_mode == AnimationMode.Manual)
                    {
                        _solidColorTransition.Transition(_sourceAnimationColors, _solidColorFrame, _currentAnimationColors, recomputeDistance: true);
                    }
                    else
                    {
                        _variableColorTransition.Transition(_sourceAnimationColors, _targetAnimationColors, _currentAnimationColors, recomputeDistance: true);
                    }
                }

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
            lock(_rootFrame)
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
            lock (_modeLock)
            {
                lock (_rootFrame)
                {
                    if (_mode == AnimationMode.SoundReactive)
                    {
                        //if (false)
                        if ((e.CurrentLevel >= -40.0d || e.AverageLevel > -35.0d) && e.AmplitudeRatio > _lastAmplitudeRatio)
                        {
                            const int multiplier = 2;
                            const int maxAnimations = 3;
                            const int minAnimations = 1;
                            var levelFactor = Math.Round(e.AverageLevel / e.CurrentLevel * multiplier);
                            var level = Convert.ToInt32(Math.Min(Math.Max(levelFactor, minAnimations), maxAnimations));
                            if (level < 3)
                            {
                                int x = 0;
                            }
                            for (int i = 0; i < level; i++)
                            {
                                Animate(forward: true, fromInput: true);
                            }
                            _frameCoolDown = DefaultFrameCooldown;
                        }
                        else
                        {
                            if (e.Bpm > 60 && e.Bpm < 200)
                            {
                                var beats = 8 * e.Bpm / 60.0d * e.Seconds;
                                _beats += beats;
                                if (_beats >= 1)
                                {
                                    _frameCoolDown = 0;
                                    _beats = 0;
                                }
                                int x = 0;
                            }
                            else
                            {
                                _frameCoolDown--;
                            }

                            if (_frameCoolDown == 0)
                            {
                                Animate(forward: true, fromInput: true);
                                _frameCoolDown = DefaultFrameCooldown;
                            }
                        }

                        _lastAmplitudeRatio = e.AmplitudeRatio;
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
            lock (_modeLock)
            {
                if (direction == ButtonDirection.Press)
                {
                    if (padNumber == padCount - 1 && _specialModesEnabled)
                    {
                        var color = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
                        SetTargetFrameToSingleColor(color);
                        SetMode(AnimationMode.Manual);


                        _pressedPads.Add(padNumber);
                    }
                    else if (padNumber == padCount - 2 && _specialModesEnabled)
                    {
                        SetMode(AnimationMode.SoundReactive);
                    }
                    else
                    {
                        _pressedPads.Add(padNumber);

                        var color = _colors[padNumber % _colors.Length];
                        SetTargetFrameToPressedPads();
                        SetMode(AnimationMode.Manual);
                    }
                    _padPressedReachedTarget = false;
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
                            SetMode(AnimationMode.Manual);
                            SetTargetFrameToPressedPads();
                        }
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
                _solidColorTransition.Transition(_sourceAnimationColors, _solidColorFrame, _currentAnimationColors, recomputeDistance: true);
            }
        }

        private void SetTargetFrameToPressedPads()
        {
            lock (_rootFrame)
            {
                Array.Copy(_currentAnimationColors, _sourceAnimationColors, _currentAnimationColors.Length);
                for (int i = 0; i < _solidColorFrame.Length; i++)
                {
                    var padNumber = _pressedPads[i % _pressedPads.Count];
                    _solidColorFrame[i] = _colors[padNumber % _colors.Length];
                }

                _solidColorTransition.Transition(_sourceAnimationColors, _solidColorFrame, _currentAnimationColors, recomputeDistance: true);
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
