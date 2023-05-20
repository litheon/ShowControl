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
        private int _maxDistance;
        private int _maxDistanceIndex;
        private readonly int[] _targetColorIndices;
        private readonly int[] _sourceColorIndices;
        private readonly ColorStat[] _skippedAnimations;
        private readonly ColorStat[] _remainingDistributed;
        private long _lastAnimationFrameTicks;
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
        private const int DefaultFrameCooldown = AudioService.SampleRate / AudioService.FftLength / 6;
        private int _frameCoolDown = 1;
        private readonly List<int> _pressedPads;
        private readonly Color[] _colors;
        bool _specialModesEnabled = false;

        public RainbowAnimation(
            int pixelCount,
            Color[] colors,
            IAudioService audioService)
        {
            _frameStart = 0;
            _rootFrame = new Pixel[pixelCount];
            _colors = colors;
            _automationTimer = new Timer(AutomaticModeFrameTick, null, Timeout.Infinite, Timeout.Infinite);
            _lastMode = AnimationMode.Automatic;
            _mode = AnimationMode.Automatic;
            _navigationColorTimer = new Timer(NavigationTimerTick, null, 1000, 1000);
            _audioService = audioService;
            _pressedPads = new List<int>();
            _targetColorIndices = new int[Math.Max(pixelCount, _colors.Length)];
            _sourceColorIndices = new int[_targetColorIndices.Length];
            _skippedAnimations = new ColorStat[_targetColorIndices.Length];
            _remainingDistributed = new ColorStat[_targetColorIndices.Length];
            int j = 0;
            for (int i = 0; i < _rootFrame.Length; i++)
            {
                _rootFrame[i] = new Pixel(_colors[j], 0, 0);
                _sourceColorIndices[i] = j;
                j = (j + 1) % _colors.Length;
            }
            j = 1;
            for (int i = 0; i < _targetColorIndices.Length; i++)
            {
                _targetColorIndices[i] = j;
                j = (j + 1) % _colors.Length;
            }
            CalculateMaxDistance();
        }

        ~RainbowAnimation()
        {
            _navigationColorTimer.Dispose();
            _automationTimer.Dispose();
        }

        private void AutomaticModeFrameTick(object state)
        {
            Animate(forward: true, fromInput: false);
            //_frameStart.InterlockedIncrement(0, _rootFrame.Length - 1);
        }

        private void CalculateMaxDistance()
        {
            _maxDistance = 0;
            for (int i = 0; i < _targetColorIndices.Length && i < _rootFrame.Length; i++)
            {
                var targetColorIndex = _targetColorIndices[i];
                var sourceColorIndex = _sourceColorIndices[i];

                if (_colors[sourceColorIndex].R != _colors[targetColorIndex].R)
                {
                    var originalRDistance = Math.Abs(_colors[targetColorIndex].R - _colors[sourceColorIndex].R);
                    DetermineMaxDistanceIndex(ref _maxDistanceIndex, ref _maxDistance, i, originalRDistance);
                }
                if (_colors[sourceColorIndex].G != _colors[targetColorIndex].G)
                {
                    var originalGDistance = Math.Abs(_colors[targetColorIndex].G - _colors[sourceColorIndex].G);
                    DetermineMaxDistanceIndex(ref _maxDistanceIndex, ref _maxDistance, i, originalGDistance);
                }
                if (_colors[sourceColorIndex].B != _colors[targetColorIndex].B)
                {
                    var originalBDistance = Math.Abs(_colors[targetColorIndex].B - _colors[sourceColorIndex].B);
                    DetermineMaxDistanceIndex(ref _maxDistanceIndex, ref _maxDistance, i, originalBDistance);
                }

                static void DetermineMaxDistanceIndex(ref int maxDistanceIndex, ref int originalMaxDistance, int i, int distance)
                {
                    if (distance > originalMaxDistance)
                    {
                        originalMaxDistance = distance;
                        maxDistanceIndex = i;
                    }
                }
            }
        }

        private void Animate(bool forward, bool fromInput)
        {
            lock (_rootFrame)
            {
                var rRemainingDistance = Math.Abs(_colors[_targetColorIndices[_maxDistanceIndex]].R - _rootFrame[_maxDistanceIndex].Color.R);
                var gRemainingDistance = Math.Abs(_colors[_targetColorIndices[_maxDistanceIndex]].G - _rootFrame[_maxDistanceIndex].Color.G);
                var bRemainingDistance = Math.Abs(_colors[_targetColorIndices[_maxDistanceIndex]].B - _rootFrame[_maxDistanceIndex].Color.B);

                var remainingDistance = Math.Max(rRemainingDistance, Math.Max(gRemainingDistance, bRemainingDistance));
                var currentTicks = DateTime.Now.Ticks;
                if (remainingDistance == 0)
                {
                    _lastAnimationFrameTicks = DateTime.Now.Ticks;
                    for (int i = 0; i < _rootFrame.Length; i++)
                    {
                        if (_rootFrame[i].Color.R != _colors[_targetColorIndices[i]].R ||
                            _rootFrame[i].Color.G != _colors[_targetColorIndices[i]].G ||
                            _rootFrame[i].Color.B != _colors[_targetColorIndices[i]].B)
                        {
                            int x = 0;
                        }
                    }

                    Array.Copy(_targetColorIndices, _sourceColorIndices, _targetColorIndices.Length);
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

                    for (int i = 0; i < _remainingDistributed.Length; i++)
                    {
                        _remainingDistributed[i].r = 0;
                        _remainingDistributed[i].g = 0;
                        _remainingDistributed[i].b = 0;
                    }

                    for (int i = 0; i < _skippedAnimations.Length; i++)
                    {
                        _skippedAnimations[i].r = 0;
                        _skippedAnimations[i].g = 0;
                        _skippedAnimations[i].b = 0;
                    }

                    for (int i = 0; i < _rootFrame.Length; i++)
                    {
                        if (_rootFrame[i].Color.R != _colors[_sourceColorIndices[i]].R ||
                            _rootFrame[i].Color.G != _colors[_sourceColorIndices[i]].G ||
                            _rootFrame[i].Color.B != _colors[_sourceColorIndices[i]].B)
                        {
                            int x = 0;
                        }
                    }

                    CalculateMaxDistance();
                    return;
                }
                else if (remainingDistance == 0)
                {
                    int x = 0;
                }

                for (int i = 0; i < _rootFrame.Length; i++)
                {
                    byte R = CalculateColorChange(_rootFrame[i].Color.R, _colors[_sourceColorIndices[i]].R, _colors[_targetColorIndices[i]].R, remainingDistance, ref _skippedAnimations[i].r, ref _remainingDistributed[i].r);
                    byte G = CalculateColorChange(_rootFrame[i].Color.G, _colors[_sourceColorIndices[i]].G, _colors[_targetColorIndices[i]].G, remainingDistance, ref _skippedAnimations[i].g, ref _remainingDistributed[i].g);
                    byte B = CalculateColorChange(_rootFrame[i].Color.B, _colors[_sourceColorIndices[i]].B, _colors[_targetColorIndices[i]].B, remainingDistance, ref _skippedAnimations[i].b, ref _remainingDistributed[i].b);
                    _rootFrame[i].Color = Color.FromArgb(0xFF, R, G, B);
                }
            }
        }

        private byte CalculateColorChange(int current, byte source, byte target, int remainingDistance,
            ref int skippedAnimations, ref int remainingDistributed)
        {
            if (current == target)
            {
                return target;
            }

            if (source < target && current < source)
            {
                int x = 0;
            }

            bool add = source < target;
            //The original distance this light had to target
            var animatesToTarget = Math.Abs(source - target);
            //The number of Animate calls that should occur before this light moves up by one to target
            var animatesPerDelta = animatesToTarget > 0 ? _maxDistance / animatesToTarget : 0;
            //The number of Animate calls that need to be made up for this light for target
            var originalRemaining = animatesToTarget > 0 && _maxDistance / animatesPerDelta < animatesToTarget ? _maxDistance / animatesPerDelta % animatesToTarget : 0;
            //On average the number of remaining to target that should be distributed per Animate for this light 
            var remainingRatePerAnimate = (double)originalRemaining / _maxDistance;
            //Should we animate to target for this light in this call?
            if (animatesPerDelta <= 1)
            {
                //yes
                current = add ? current + 1 : current - 1;
            }
            else if (animatesPerDelta > 1)
            {
                if (skippedAnimations < animatesPerDelta - 1)
                {
                    skippedAnimations++;
                }
                else
                {
                    current = add ? current + 1 : current - 1;
                    skippedAnimations = 0;
                }
            }

            if ((double)(originalRemaining - remainingDistributed) / (remainingDistance) > remainingRatePerAnimate)
            {
                current = add ? current + 1 : current - 1;
                remainingDistributed++;
            }

            if (current == target)
            {
                int x = 0;
            }

            if (current > 255 || current < 0)
            {
                int x = 0;
            }

            return (byte)current;
        }

        private void NavigationTimerTick(object state)
        {
            navigationColorIndex = (navigationColorIndex + 1) % _colors.Length;
            SetNavigationColor(_colors[navigationColorIndex]);
        }

        private void StartTimers()
        {
            _automationTimer.Change(5, 5);
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
                if (_mode == AnimationMode.Automatic)
                {
                    _automationTimer.Change(5, 5);
                    //_automationTimer.Change(1, 1);
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
            if (_mode == AnimationMode.SoundReactive)
            {
                if (e.CurrentLevel >= -30.0d)
                {
                    var level = Convert.ToInt32(Math.Min(Math.Max(Math.Round(e.CurrentLevel / e.AverageLevel) * 3, 1), 3));
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
                    SetFrameWithSingleColor(color);
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
                    SetFrameWithSingleColor(color);
                    _pressedPads.Add(padNumber);
                }
            }
            else if (direction == ButtonDirection.Release)
            {
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
                for (int i = 0; i < 10; i++)
                {
                    Animate(direction == RotaryDirection.Up, fromInput: true);
                }
                
                //SetMode(AnimationMode.Manual);
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

    public struct ColorStat
    {
        public int r;
        public int g;
        public int b;
    }
}
