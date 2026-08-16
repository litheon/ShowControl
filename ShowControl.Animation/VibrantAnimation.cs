using ShowControl.Devices.Audio;
using ShowControl.Devices.Input;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using ShowControl.Utilities;
using System.Runtime.InteropServices;

namespace ShowControl.Animation
{
    public class VibrantAnimation : MidiAwareAnimation, IKeyAnimation, IDisposable
    {
        private readonly IAudioService _audioService;
        private const int DefaultFrameCooldown = AudioService.SampleRate / AudioService.FftLength / 2;
        private readonly int _pixelCount = 0;
        private readonly object _currentFrameLock = new object();
        private Pixel[] _currentFrame;
        private Pixel[] _nextFrame;
        private readonly object _targetFrameLock = new object();
        private Color[] _targetFrame;

        private readonly Random _random;
        public int _frameCoolDown;
        private long _lastBassOverride = 0;
        private const int BassOverrideSeconds = 10;

        private readonly CancellationToken _cancellationToken;
        private readonly Thread _animationThread;
        private int _navigationColorIndex = 0;
        private int _frameCountAnimationThreshold = 4;
        private int _currentFrameCountAnimationThreshold = 0;
        private readonly int AnimationFrameCountMaximum = 3;

        private readonly object _modeLock = new object();
        private AnimationMode _lastMode;
        private AnimationMode _mode;
        private readonly List<int> _pressedPads;
#pragma warning disable IDE0052 // Remove unread private members
        private readonly Timer _navigationColorTimer;
#pragma warning restore IDE0052 // Remove unread private members
        private readonly Color[] _vibrantColors;
        public VibrantAnimation(int pixelCount, IAudioService audioService, Color[] colors, CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _mode = AnimationMode.Automatic;
            _pixelCount = pixelCount;
            _audioService = audioService;
            _pressedPads = new List<int>();
            _random = new Random();
            _currentFrame = new Pixel[pixelCount];
            _nextFrame = new Pixel[pixelCount];
            _targetFrame = new Color[pixelCount];
            _navigationColorTimer = new Timer(NavigationTimerTick, null, 1000, 1000);
            _animationThread = new Thread(AutomationThreadStart);
            _animationThread.Start();
            _vibrantColors = colors;
            for (int i = 0; i < pixelCount; i++)
            {
                _currentFrame[i] = new Pixel(_vibrantColors[_random.Next() % _vibrantColors.Length], 0, 0, 0x00);
            }
            for (int i = 0; i < pixelCount; i++)
            {
                _targetFrame[i] = _vibrantColors[_random.Next() % _vibrantColors.Length];
            }
        }

        ~VibrantAnimation()
        {

        }

        private void AutomationThreadStart(object state)
        {
            while(!_cancellationToken.IsCancellationRequested)
            {
                if (_mode == AnimationMode.Automatic)
                {
                    if (_currentFrameCountAnimationThreshold.InterlockedGet() == 0)
                    {
                        //AnimateFrame();
                        Interlocked.Exchange(ref _currentFrameCountAnimationThreshold, _frameCountAnimationThreshold.InterlockedGet());
                    }
                    else
                    {
                        _currentFrameCountAnimationThreshold.InterlockedDecrement(0, AnimationFrameCountMaximum);
                    }
                }
                else if (_mode == AnimationMode.Manual)
                {
                    //AnimateFrame();
                }
                Thread.Sleep(1);
            }
        }

        private void OnAudioFrameProcessed(object sender, AudioFrameProcessedEventArgs e)
        {
            //graph the FFT spectra
            //option 1: each light represents a certain width of the spectra, and specific colors map to specific ranges
            //option 2: from left to right each frequency range is graphed on the lights, with, for example the left most
            //light being off is zero while the right most light being on is 100
            //large bands will be mapped to individual colors(e.g.RGB), and those bands' amplitude will be graphed from L to R
            using (e)
            {
                var secondsSinceLastBassOverride = (DateTime.Now.Ticks - _lastBassOverride) / TimeSpan.TicksPerSecond;

                if (_lastBassOverride == 0 || secondsSinceLastBassOverride >= BassOverrideSeconds)
                {
                    if (e.AverageLevel < -42.0d)
                    {
                        Interlocked.Exchange(ref _frameCountAnimationThreshold, AnimationFrameCountMaximum);
                    }
                    else if (e.Bpm > 50)
                    {
                        var bpmMatch = 1000 / (e.Bpm / 60 * 16);
                        var result = Convert.ToInt32(Math.Round(bpmMatch));
                        Interlocked.Exchange(ref _frameCountAnimationThreshold, result);
                    }
                    else if (e.Bpm > 0)
                    {
                        Interlocked.Exchange(ref _frameCountAnimationThreshold, AnimationFrameCountMaximum);
                    }
                }

                lock (_modeLock)
                {
                    if (_mode == AnimationMode.Manual)
                    {
                        return;
                    }
                }

                if (1 / (e.CurrentLevel / e.AverageLevel) > 1.3 && e.CurrentLevel > -35.0d && (/*_frameCoolDown <= 0 || */secondsSinceLastBassOverride >= BassOverrideSeconds))
                {
                    SetNextFrameWithSingleColor(_vibrantColors[_random.Next() % _vibrantColors.Length]);

                    //if (secondsSinceLastBassOverride >= BassOverrideSeconds && _frameCoolDown > 0)
                    //{
                    Interlocked.Exchange(ref _frameCountAnimationThreshold, 0);
                    Interlocked.Exchange(ref _currentFrameCountAnimationThreshold, 0);
                    _lastBassOverride = DateTime.Now.Ticks;
                    //}
                    //else
                    //{
                    //    _frameCoolDown = DefaultFrameCooldown;
                    //}
                }
                //else if (_frameCoolDown >= 1)
                //{
                //    Interlocked.Exchange(ref _currentFrameCountAnimationThreshold, 0);
                //    _frameCoolDown--;
                //}
                //else if (_frameCoolDown == 0)
                //{
                //    _frameCoolDown = -1;
                //}
            }
        }

        private void SetMode(AnimationMode mode)
        {
            lock(_modeLock)
            {
                if (mode != _mode)
                {
                    _lastMode = _mode;
                }
                _mode = mode;
            }
        }

        private void RestoreMode()
        {
            lock(_modeLock)
            {
                if (_mode == AnimationMode.Manual)
                {
                    _mode = _lastMode;
                }
            }
        }

        public Pixel[] GetNextFrame()
        {
            AnimateFrame();
            return _currentFrame;
        }

        /// <summary>
        /// Frame production hook. The shader-sampling implementation that used to live
        /// here moved to <see cref="ShaderAnimation"/>; the audio-reactive path below
        /// (_targetFrame / _frameCountAnimationThreshold) was already inert when it was
        /// split out, so this class currently emits its palette-seeded initial frame
        /// unchanged until the audio-reactivity rework lands.
        /// </summary>
        private void AnimateFrame()
        {
        }

        private void NavigationTimerTick(object state)
        {
            _navigationColorIndex = (_navigationColorIndex + 1) % _vibrantColors.Length;
            SetNavigationColor(_vibrantColors[_navigationColorIndex]);
        }

        public override void OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            //throw new NotImplementedException();
        }

        private PadState[] GetPadStates(int count)
        {
            var padStates = new PadState[count];
            for (int i = 0; i < padStates.Length; i++)
            {
                padStates[i] = new PadState(PadIllumination.Solid, _vibrantColors[i % _vibrantColors.Length]);
            }
            return padStates;
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
            _audioService.AudioFrameProcessed += OnAudioFrameProcessed;
            PadState[] padStates;
            if (inputDevice.Capabilities.HasFlag(DeviceCapabilities.Pads))
            {
                padStates = GetPadStates(inputDevice.PadCount);
            }
            else
            {
                padStates = new PadState[0];
            }
            AssignPadStates(padStates);
            return new DeviceState(padStates, new ButtonLightState[0]);
        }

        public override void Stop()
        {
            _audioService.AudioFrameProcessed -= OnAudioFrameProcessed;
        }

        public override void RotaryTurned(RotaryDirection direction, byte rotaryNumber, byte? value)
        {
        }

        public override void PadPressed(ButtonDirection direction, byte padNumber, int padCount)
        {
            if (direction == ButtonDirection.Press)
            {
                _pressedPads.Add(padNumber);
                SetMode(AnimationMode.Manual);
                SetTargetFrameWithPressedPads();
            }
            else if (direction == ButtonDirection.Release)
            {
                _pressedPads.Remove(padNumber);
                if (_pressedPads.Count == 0)
                {
                    RestoreMode();
                }
                else
                {
                    SetTargetFrameWithPressedPads();
                }
            }
        }

        private void SetNextFrameWithSingleColor(Color color)
        {
            Color[] colors = new Color[_pixelCount];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = color;
            }
            lock(_targetFrameLock)
            {
                _targetFrame = colors;
            }
        }

        private void SetTargetFrameWithPressedPads()
        {
            Color[] colors = new Color[_pixelCount];
            for (int i = 0; i < colors.Length; i++)
            {
                var padNumber = _pressedPads[i % _pressedPads.Count];
                colors[i] = _vibrantColors[padNumber % _vibrantColors.Length];
            }
            lock (_targetFrameLock)
            {
                _targetFrame = colors;
            }
        }

        public void Dispose()
        {
            Stop();
            _navigationColorTimer.Dispose();
        }
    }
}
