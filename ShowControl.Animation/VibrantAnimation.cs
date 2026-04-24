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
using ShowControl.Graphics;

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
        private string _shader;
        private readonly List<int> _pressedPads;
#pragma warning disable IDE0052 // Remove unread private members
        private readonly Timer _navigationColorTimer;
#pragma warning restore IDE0052 // Remove unread private members
        private readonly Color[] _vibrantColors;
        private IGraphicsHost _graphicsHost;
        public VibrantAnimation(int pixelCount, IAudioService audioService, IGraphicsHost graphicsHost, string shader, Color[] colors, CancellationToken cancellationToken)
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
            _graphicsHost = graphicsHost;
            _shader = shader;
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
            lock (_modeLock)
            {
                //if (ShowControl.Graphics.GraphicsHost.Bytes == null)
                //{
                //    return;
                //}

                using var memory = _graphicsHost.GetRenderedImage();
                if (memory == RenderedImageHandle<byte>.Empty)
                {
                    return;
                }

                var span = memory.Memory.Span;

                var width = _graphicsHost.Width;
                var height = _graphicsHost.Height;
                var initial = (height / 2) * width * 4 - width * 120 * 4;
                int offset = 0;
                int pixelStride = 0;
                for (int i = 0; i < _pixelCount; i++)
                {
                    //_nextFrame[i].Color = Color.FromArgb(0, ShowControl.Graphics.HelloWorldGraphicsHost.Bytes[len + 4 * (i * 6) + 2], ShowControl.Graphics.HelloWorldGraphicsHost.Bytes[len + 4 * (i * 6) + 1], ShowControl.Graphics.HelloWorldGraphicsHost.Bytes[len + 4 * (i * 6)]);
                    //

                    var rowOffset = offset * width * 4;
                    var r = initial + pixelStride + rowOffset;
                    var g = initial + pixelStride + rowOffset + 1;
                    var b = initial + pixelStride + rowOffset + 2;
                    _nextFrame[i].Color = Color.FromArgb(0, span[r], span[g], span[b]);
                    if (i == 479 || i == 959 || i == 1439)
                    {
                        offset += 10;
                        pixelStride = 0;
                    }
                    else
                    {
                        pixelStride += 4;
                    }
                }
                var temp = _currentFrame;
                _currentFrame = _nextFrame;
                _nextFrame = temp;
            }
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
            _graphicsHost.SetShader(_shader);
            ResetShaderParams();
            return new DeviceState(padStates, new ButtonLightState[0]);
        }

        private void ResetShaderParams()
        {
            _graphicsHost.DistortionStrength = 0.12f;

            if (_shader == ShowControl.Graphics.Shaders.Kaleidoscope)
            {
                _graphicsHost.ShaderParam1 = 10.0f;  // sectors (N)
                _graphicsHost.ShaderParam2 = 0.6f;   // animation speed
                _graphicsHost.ShaderParam3 = 3.0f;   // radial color ring density (k base)
                _graphicsHost.ShaderParam4 = 10.0f;  // flow pattern frequency
            }
            else if (_shader == ShowControl.Graphics.Shaders.Clown)
            {
                _graphicsHost.ShaderParam1 = 16.0f;  // blade count
                _graphicsHost.ShaderParam2 = 0.0f;   // spin angle offset
                _graphicsHost.ShaderParam3 = 0.0f;   // blade hue offset
                _graphicsHost.ShaderParam4 = 0.29f;  // dot radius
            }
        }

        public override void Stop()
        {
            _audioService.AudioFrameProcessed -= OnAudioFrameProcessed;
        }

        public override void RotaryTurned(RotaryDirection direction, byte rotaryNumber, byte? value)
        {
            if (rotaryNumber >= 1 && rotaryNumber <= 4)
            {
                AdjustShaderParam(rotaryNumber - 1, direction == RotaryDirection.Up);
            }
        }

        private void AdjustShaderParam(int paramIndex, bool increase)
        {
            float step, min, max;
            if (_shader == ShowControl.Graphics.Shaders.Kaleidoscope)
            {
                (step, min, max) = paramIndex switch
                {
                    0 => (0.014f, 3.0f,  24.0f),  // sectors          (÷2)
                    1 => (0.00025f, 0.05f, 3.0f), // speed            (÷4)
                    2 => (0.04f,  0.5f,  12.0f),  // k base (ring density)
                    3 => (0.1f,   2.0f,  30.0f),  // flow frequency
                    _ => (0f, 0f, 0f)
                };
            }
            else if (_shader == ShowControl.Graphics.Shaders.Clown)
            {
                (step, min, max) = paramIndex switch
                {
                    0 => (0.017f, 16.0f, 30.0f),   // blade count
                    1 => (0.0125f, -500.0f, 500.0f), // spin angle offset
                    2 => (0.008f, -500.0f, 500.0f), // blade hue offset
                    3 => (0.003f, 0.05f, 0.45f),  // dot radius
                    _ => (0f, 0f, 0f)
                };
            }
            else
            {
                return;
            }

            float current = paramIndex switch
            {
                0 => _graphicsHost.ShaderParam1,
                1 => _graphicsHost.ShaderParam2,
                2 => _graphicsHost.ShaderParam3,
                3 => _graphicsHost.ShaderParam4,
                _ => 0f
            };

            float next = Math.Clamp(current + (increase ? step : -step), min, max);

            switch (paramIndex)
            {
                case 0: _graphicsHost.ShaderParam1 = next; break;
                case 1: _graphicsHost.ShaderParam2 = next; break;
                case 2: _graphicsHost.ShaderParam3 = next; break;
                case 3: _graphicsHost.ShaderParam4 = next; break;
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
            var graphicsColors = new Color[Math.Min(_pressedPads.Count, 6)];
            for (int i = 0; i < graphicsColors.Length; i++)
            {
                graphicsColors[i] = _vibrantColors[_pressedPads[i] % _vibrantColors.Length];
            }
            _graphicsHost.Colors = graphicsColors;
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
