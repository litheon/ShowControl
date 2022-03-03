using ShowControl.Devices.Audio;
using ShowControl.Devices.Input;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using ShowControl.Utilities;

namespace ShowControl.Animation
{
    public class VibrantAnimation : MidiAwareAnimation
    {
        private readonly IAudioService _audioService;
        private const int defaultFrameCooldown = AudioService.SampleRate / AudioService.FftLength / 6;
        private readonly int _pixelCount = 0;
        private readonly object _currentFrameLock = new object();
        private Pixel[] _currentFrame;
        private Pixel[] _nextFrame;
        private readonly object _targetFrameLock = new object();
        private Color[] _targetFrame;

        private readonly Random _random;
        private double _lastFft;
        public int _frameCoolDown;

        private readonly Thread _animationThread;
        int navigationColorIndex = 0;
        private int _frameCountAnimationThreshold = 0;
        private int _currentFrameCountAnimationThreshold = 0;
        private readonly int AnimationFrameCountMaximum = 12;

        private readonly object _modeLock = new object();
        private AnimationMode _lastMode;
        private AnimationMode _mode;
        private readonly List<int> _pressedPads;
#pragma warning disable IDE0052 // Remove unread private members
        private readonly Timer _navigationColorTimer;
#pragma warning restore IDE0052 // Remove unread private members
        private readonly Color[] _vibrantColors = new Color[]
        {
            Color.Red,
            Color.FromArgb(255, 0, 137),
            Color.FromArgb(255, 0, 35),
            Color.FromArgb(67, 112, 0),
            Color.FromArgb(255, 74, 0),
            Color.FromArgb(9, 254, 182)
        };
        public VibrantAnimation(int pixelCount, IAudioService audioService)
        {
            _mode = AnimationMode.Automatic;
            _pixelCount = pixelCount;
            _audioService = audioService;
            _pressedPads = new List<int>();
            _random = new Random();
            _currentFrame = new Pixel[pixelCount];
            _nextFrame = new Pixel[pixelCount];
            _targetFrame = new Color[pixelCount];
            _navigationColorTimer = new Timer(NavigationTimerTick, null, 1000, 1000);
            _animationThread = new Thread(AnimationTimerTick);
            _animationThread.Start();
            for (int i = 0; i < pixelCount; i++)
            {
                _currentFrame[i] = new Pixel(_vibrantColors[_random.Next() % _vibrantColors.Length], 0, 0);
            }
            for (int i = 0; i < pixelCount; i++)
            {
                _targetFrame[i] = _vibrantColors[_random.Next() % _vibrantColors.Length];
            }
        }

        private void AnimationTimerTick(object state)
        {
            while(true)
            {
                if (_mode == AnimationMode.Automatic)
                {
                    if (_currentFrameCountAnimationThreshold.InterlockedGet() == 0)
                    {
                        AnimateFrame();
                        Interlocked.Exchange(ref _currentFrameCountAnimationThreshold, _frameCountAnimationThreshold.InterlockedGet());
                    }
                    else
                    {
                        _currentFrameCountAnimationThreshold.InterlockedDecrement(0, AnimationFrameCountMaximum);
                    }
                }
                else if (_mode == AnimationMode.Manual)
                {
                    AnimateFrame();
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
            //large bands will be mapped to individual colors (e.g. RGB), and those bands' amplitude will be graphed from L to R
            var spectrum = e.AudioSpectrum;
            
            var one = e.AudioSpectrum[1].Y;
            var two = e.AudioSpectrum[2].Y;
            var three = e.AudioSpectrum[3].Y;

            var current = (one + two + three) / 3;

            var fullSignal = e.AudioSpectrum[0].Y;
            if (fullSignal < -100)
            {
                Interlocked.Exchange(ref _frameCountAnimationThreshold, AnimationFrameCountMaximum);
            }
            else if (e.Bpm > 50)
            {
                var bpmMatch = 1000 / (e.Bpm / 60 * 32);
                var result = Convert.ToInt32(Math.Round(bpmMatch));
                Interlocked.Exchange(ref _frameCountAnimationThreshold, result);
            }
            else if (e.Bpm > 0)
            {
                Interlocked.Exchange(ref _frameCountAnimationThreshold, AnimationFrameCountMaximum);
            }

            lock (_modeLock)
            {
                if (_mode == AnimationMode.Manual)
                {
                    return;
                }
            }

            if (current > _lastFft * 1.2 && current > -25 && _frameCoolDown <= 0)
            {
                SetNextFrameWithSingleColor(_vibrantColors[_random.Next() % _vibrantColors.Length]);
                _frameCoolDown = defaultFrameCooldown;
            }
            else if (_frameCoolDown >= 1)
            {
                Interlocked.Exchange(ref _currentFrameCountAnimationThreshold, 0);
                _frameCoolDown--;
            }
            else if (_frameCoolDown == 0)
            {
                _frameCoolDown = -1;
            }
            _lastFft = current;
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

        public override Pixel[] GetNextFrame()
        {
            return _currentFrame;
        }

        private void AnimateFrame()
        {
            AnimationMode mode;
            Color[] targetFrame;
            lock(_targetFrameLock)
            {
                targetFrame = _targetFrame;
            }
            lock(_currentFrameLock)
            {
                Array.Copy(_currentFrame, _nextFrame, _currentFrame.Length);
            }
            lock (_modeLock)
            {
                mode = _mode;
            }
            for (int i = 0; i < _pixelCount; i++)
            {
                var existingColor = _nextFrame[i].Color;
                var targetColor = targetFrame[i];
                var R = existingColor.R;
                var G = existingColor.G;
                var B = existingColor.B;
                if (R < targetColor.R)
                {
                    R++;
                }
                if (R > targetColor.R)
                {
                    R--;
                }
                else if (B < targetColor.B)
                {
                    B++;
                }
                else if (B > targetColor.B)
                {
                    B--;
                }
                else if (G < targetColor.G)
                {
                    G++;
                }
                else if (G > targetColor.G)
                {
                    G--;
                }
                else if (mode == AnimationMode.Automatic)
                {
                    lock(_targetFrameLock)
                    {
                        this._targetFrame[i] = _vibrantColors[_random.Next() % _vibrantColors.Length];
                    }
                }

                _nextFrame[i].Color = Color.FromArgb(R, G, B);
            }
            var temp = _currentFrame;
            _currentFrame = _nextFrame;
            _nextFrame = temp;
        }

        private void NavigationTimerTick(object state)
        {
            navigationColorIndex = (navigationColorIndex + 1) % _vibrantColors.Length;
            SetNavigationColor(_vibrantColors[navigationColorIndex]);
        }

        public override void OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            //throw new NotImplementedException();
        }

        private System.Drawing.Color[] GetPadColors(int count)
        {
            var padColors = new System.Drawing.Color[count];
            for (int i = 0; i < padColors.Length - 2; i++)
            {
                padColors[i] = _vibrantColors[i % _vibrantColors.Length];
            }
            padColors[^2] = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
            padColors[^1] = System.Drawing.Color.FromArgb(0x00, 0x00, 0x00, 0x00);
            return padColors;
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
            _audioService.AudioFrameProcessed += OnAudioFrameProcessed;
            Color[] padColors;
            if (inputDevice.Capabilities.HasFlag(DeviceCapabilities.Pads))
            {
                padColors = GetPadColors(inputDevice.PadCount);
            }
            else
            {
                padColors = new Color[0];
            }
            AssignPadColors(padColors);
            return new DeviceState(padColors, new ButtonLightState[0]);
        }

        public override void Stop()
        {
            _audioService.AudioFrameProcessed -= OnAudioFrameProcessed;
        }

        public override void RotaryTurned(RotaryDirection direction, byte rotaryNumber, byte? value)
        {
            if (rotaryNumber == 1)
            {
                if (direction == RotaryDirection.Up)
                {
                    var threshold = _frameCountAnimationThreshold.InterlockedIncrement(0, 4);
                    _currentFrameCountAnimationThreshold = threshold;
                }
                else if (direction == RotaryDirection.Down)
                {
                    var threshold = _frameCountAnimationThreshold.InterlockedDecrement(0, 4);
                    _currentFrameCountAnimationThreshold = threshold;
                }
            }
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

        private void SetFrameWithSingleColor(Color color)
        {
            Pixel[] colors = new Pixel[_pixelCount];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Pixel(color, 0x00, 0x00);
            }
            SetFrame(colors);
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
                colors[i] = _vibrantColors[padNumber % _vibrantColors.Length]; //new Pixel(_vibrantColors[padNumber % _vibrantColors.Length], 0x00, 0x00);
            }
            lock (_targetFrameLock)
            {
                _targetFrame = colors;
            }
        }

        private void SetFrame(Pixel[] frame)
        {
            lock(_currentFrameLock)
            {
                _currentFrame = frame;
            }
        }
    }
}
