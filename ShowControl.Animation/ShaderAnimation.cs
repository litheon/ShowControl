using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShowControl.Devices.Input;
using ShowControl.Graphics;
using ShowControl.PixelMapping;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace ShowControl.Animation
{
    /// <summary>
    /// GPU shader-driven key animation. Owns everything that talks to the graphics
    /// host: selecting the shader, holding its tunable parameters (rotary-adjustable,
    /// nudged back toward their defaults by the render operator timer), feeding it the
    /// pad colors, and sampling the rendered image back out into the pixel frame.
    /// <para>
    /// Where each pixel samples from is supplied by a <see cref="PixelSampleMap"/> compiled from the
    /// saved pixel map, not computed here. The map is immutable and swapped with a single volatile
    /// write, so the editor can drag a fixture and have the lights follow without a lock on the
    /// render thread.
    /// </para>
    /// </summary>
    public class ShaderAnimation : MidiAwareAnimation, IKeyAnimation, IDisposable
    {
        private readonly IGraphicsHost _graphicsHost;
        private readonly string _shader;
        private readonly Color[] _shaderColors;
        private readonly Random _random;

        private readonly ILogger _logger;
        private PixelSampleMap _sampleMap;
        private RgbSample[] _samples;
        private bool _loggedImageSizeMismatch;

        private readonly object _currentFrameLock = new object();
        private Pixel[] _currentFrame;
        private Pixel[] _nextFrame;

        private readonly List<int> _pressedPads;
        private int _navigationColorIndex = 0;
#pragma warning disable IDE0052 // Remove unread private members
        private readonly Timer _navigationColorTimer;
#pragma warning restore IDE0052 // Remove unread private members

        private float _defaultDistortionStrength;
        private float _defaultParam1, _defaultParam2, _defaultParam3, _defaultParam4;

        public ShaderAnimation(
            PixelSampleMap sampleMap,
            IGraphicsHost graphicsHost,
            string shader,
            Color[] colors,
            ILogger logger = null)
        {
            _sampleMap = sampleMap ?? throw new ArgumentNullException(nameof(sampleMap));
            _logger = logger ?? NullLogger.Instance;
            _graphicsHost = graphicsHost;
            _shader = shader;
            _shaderColors = colors;
            _random = new Random();
            _pressedPads = new List<int>();

            int pixelCount = sampleMap.PixelCount;
            _currentFrame = new Pixel[pixelCount];
            _nextFrame = new Pixel[pixelCount];
            _samples = new RgbSample[pixelCount];
            _navigationColorTimer = new Timer(NavigationTimerTick, null, 1000, 1000);
            for (int i = 0; i < pixelCount; i++)
            {
                _currentFrame[i] = new Pixel(_shaderColors[_random.Next() % _shaderColors.Length], 0, 0, 0x00);
            }
        }

        /// <summary>
        /// Publishes new pixel geometry. Called from the UI thread while the render thread is
        /// running: the map is immutable, so a single volatile write is enough and a frame in
        /// flight simply finishes with the previous geometry.
        /// </summary>
        public void SetSampleMap(PixelSampleMap sampleMap)
        {
            if (sampleMap == null) throw new ArgumentNullException(nameof(sampleMap));
            Volatile.Write(ref _sampleMap, sampleMap);
        }

        public Pixel[] GetNextFrame()
        {
            AnimateFrame();
            return _currentFrame;
        }

        /// <summary>
        /// Samples the shader's rendered image into the pixel frame, one lookup per pixel through
        /// the compiled sample map.
        /// </summary>
        private void AnimateFrame()
        {
            var sampleMap = Volatile.Read(ref _sampleMap);

            lock (_currentFrameLock)
            {
                if (sampleMap.ImageWidth != _graphicsHost.Width || sampleMap.ImageHeight != _graphicsHost.Height)
                {
                    if (!_loggedImageSizeMismatch)
                    {
                        // Once only: this is a per-frame path.
                        _loggedImageSizeMismatch = true;
                        _logger.LogError(
                            "[ShaderAnimation] Pixel map is authored for a {MapWidth}x{MapHeight} shader image but the render target is {HostWidth}x{HostHeight}; not sampling.",
                            sampleMap.ImageWidth, sampleMap.ImageHeight, _graphicsHost.Width, _graphicsHost.Height);
                    }
                    return;
                }

                EnsureCapacity(sampleMap.PixelCount);
                Array.Copy(_currentFrame, _nextFrame, _currentFrame.Length);

                using var memory = _graphicsHost.GetRenderedImage();
                if (memory == RenderedImageHandle<byte>.Empty)
                {
                    return;
                }

                sampleMap.Sample(memory.Memory.Span, _samples);
                for (int i = 0; i < _nextFrame.Length; i++)
                {
                    var sample = _samples[i];
                    _nextFrame[i].Color = Color.FromArgb(0, sample.R, sample.G, sample.B);
                }

                var temp = _currentFrame;
                _currentFrame = _nextFrame;
                _nextFrame = temp;
            }
        }

        /// <summary>
        /// Resizes the frame buffers after a structural map change. Held under the frame lock so a
        /// caller reading the current frame never sees a half-swapped pair.
        /// </summary>
        private void EnsureCapacity(int pixelCount)
        {
            if (_currentFrame.Length == pixelCount)
            {
                return;
            }

            var resized = new Pixel[pixelCount];
            Array.Copy(_currentFrame, resized, Math.Min(_currentFrame.Length, pixelCount));
            _currentFrame = resized;
            _nextFrame = new Pixel[pixelCount];
            _samples = new RgbSample[pixelCount];
        }

        private void NavigationTimerTick(object state)
        {
            _navigationColorIndex = (_navigationColorIndex + 1) % _shaderColors.Length;
            SetNavigationColor(_shaderColors[_navigationColorIndex]);
        }

        private PadState[] GetPadStates(int count)
        {
            var padStates = new PadState[count];
            for (int i = 0; i < padStates.Length; i++)
            {
                padStates[i] = new PadState(PadIllumination.Solid, _shaderColors[i % _shaderColors.Length]);
            }
            return padStates;
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
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

        public override void Stop()
        {
        }

        public override void OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
        }

        public override void RotaryTurned(RotaryDirection direction, byte rotaryNumber, byte? value)
        {
            if (rotaryNumber >= 1 && rotaryNumber <= 4)
            {
                AdjustShaderParam(rotaryNumber - 1, direction == RotaryDirection.Up);
            }
        }

        public override void PadPressed(ButtonDirection direction, byte padNumber, int padCount)
        {
            if (direction == ButtonDirection.Press)
            {
                _pressedPads.Add(padNumber);
            }
            else if (direction == ButtonDirection.Release)
            {
                _pressedPads.Remove(padNumber);
            }
            var graphicsColors = new Color[Math.Min(_pressedPads.Count, 6)];
            for (int i = 0; i < graphicsColors.Length; i++)
            {
                graphicsColors[i] = _shaderColors[_pressedPads[i] % _shaderColors.Length];
            }
            _graphicsHost.Colors = graphicsColors;
        }

        /// <summary>
        /// Moves each shader parameter a fraction toward its default value.
        /// Called periodically by the render operator timer.
        /// </summary>
        public void NudgeParamsTowardDefaults()
        {
            float s1, s2, s3, s4;
            if (_shader == ShowControl.Graphics.Shaders.Kaleidoscope)
            {
                s1 = 0.0002f; s2 = 0.0001f; s3 = 0.0004f; s4 = 0.001f;
            }
            else if (_shader == ShowControl.Graphics.Shaders.Clown)
            {
                s1 = 0.00017f; s2 = 0.000125f; s3 = 0.00008f; s4 = 0.00003f;
            }
            else return;

            _graphicsHost.ShaderParam1 = StepToward(_graphicsHost.ShaderParam1, _defaultParam1, s1);
            _graphicsHost.ShaderParam2 = StepToward(_graphicsHost.ShaderParam2, _defaultParam2, s2);
            _graphicsHost.ShaderParam3 = StepToward(_graphicsHost.ShaderParam3, _defaultParam3, s3);
            _graphicsHost.ShaderParam4 = StepToward(_graphicsHost.ShaderParam4, _defaultParam4, s4);
        }

        private static float StepToward(float current, float target, float step)
        {
            float diff = target - current;
            if (Math.Abs(diff) <= step) return target;
            return current + Math.Sign(diff) * step;
        }

        private void ResetShaderParams()
        {
            _defaultDistortionStrength = 0.12f;

            if (_shader == ShowControl.Graphics.Shaders.Kaleidoscope)
            {
                _defaultParam1 = 2.5f;   // spiral twist
                _defaultParam2 = 0.0f;   // animation time offset
                _defaultParam3 = 3.0f;   // radial color ring density (k base)
                _defaultParam4 = 10.0f;  // flow pattern frequency
            }
            else if (_shader == ShowControl.Graphics.Shaders.Clown)
            {
                _defaultParam1 = 16.0f;  // blade count
                _defaultParam2 = 0.0f;   // spin angle offset
                _defaultParam3 = 0.0f;   // blade hue offset
                _defaultParam4 = 0.29f;  // dot radius
            }

            _graphicsHost.DistortionStrength = _defaultDistortionStrength;
            _graphicsHost.ShaderParam1 = _defaultParam1;
            _graphicsHost.ShaderParam2 = _defaultParam2;
            _graphicsHost.ShaderParam3 = _defaultParam3;
            _graphicsHost.ShaderParam4 = _defaultParam4;
        }

        private void AdjustShaderParam(int paramIndex, bool increase)
        {
            float step, min, max;
            if (_shader == ShowControl.Graphics.Shaders.Kaleidoscope)
            {
                (step, min, max) = paramIndex switch
                {
                    0 => (0.013f, 0.5f, 8.0f),      // spiral twist
                    1 => (0.01f, -500.0f, 500.0f),     // animation time offset
                    2 => (0.0094f, 0.5f,  12.0f), // k base (ring density)
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

        public void Dispose()
        {
            Stop();
            _navigationColorTimer.Dispose();
        }
    }
}
