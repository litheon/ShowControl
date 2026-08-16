using ShowControl.Devices.Input;
using ShowControl.Graphics;
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
    /// </summary>
    public class ShaderAnimation : MidiAwareAnimation, IKeyAnimation, IDisposable
    {
        private readonly int _pixelCount;
        private readonly IGraphicsHost _graphicsHost;
        private readonly string _shader;
        private readonly Color[] _shaderColors;
        private readonly Random _random;

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

        public ShaderAnimation(int pixelCount, IGraphicsHost graphicsHost, string shader, Color[] colors)
        {
            _pixelCount = pixelCount;
            _graphicsHost = graphicsHost;
            _shader = shader;
            _shaderColors = colors;
            _random = new Random();
            _pressedPads = new List<int>();
            _currentFrame = new Pixel[pixelCount];
            _nextFrame = new Pixel[pixelCount];
            _navigationColorTimer = new Timer(NavigationTimerTick, null, 1000, 1000);
            for (int i = 0; i < pixelCount; i++)
            {
                _currentFrame[i] = new Pixel(_shaderColors[_random.Next() % _shaderColors.Length], 0, 0, 0x00);
            }
        }

        public Pixel[] GetNextFrame()
        {
            AnimateFrame();
            return _currentFrame;
        }

        /// <summary>
        /// Samples the shader's rendered image into the pixel frame. Reads a band of
        /// rows starting 80 rows above the vertical center, one row group per strip.
        /// </summary>
        private void AnimateFrame()
        {
            lock (_currentFrameLock)
            {
                Array.Copy(_currentFrame, _nextFrame, _currentFrame.Length);

                using var memory = _graphicsHost.GetRenderedImage();
                if (memory == RenderedImageHandle<byte>.Empty)
                {
                    return;
                }

                var span = memory.Memory.Span;

                var width = _graphicsHost.Width;
                var height = _graphicsHost.Height;
                var initial = (height / 2) * width * 4 - width * 80 * 4;
                int offset = 0;
                int pixelStride = 0;
                for (int i = 0; i < _pixelCount; i++)
                {
                    var rowOffset = offset * width * 4;
                    var r = initial + pixelStride + rowOffset;
                    var g = initial + pixelStride + rowOffset + 1;
                    var b = initial + pixelStride + rowOffset + 2;
                    int posInStrip = i % 720;
                    if (posInStrip >= 708) // last 12 pixels of each strip
                    {
                        _nextFrame[i].Color = Color.FromArgb(0, 0, 0, 0);
                    }
                    else
                    {
                        _nextFrame[i].Color = Color.FromArgb(0, span[r], span[g], span[b]);
                    }
                    if (i == 719 || i == 1439 || i == 2159 || i == 2879 || i == 3599 || i == 4319 || i == 5039 ||
                        i == 5759 || i == 6479 || i == 7199 || i == 7919 || i == 8639 || i == 9359 || i == 10079 ||
                        i == 10799)
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
