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
    public class ColorPaletteAnimation : MidiAwareAnimation, IKeyAnimation, IDisposable
    {
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
        private const int DefaultFrameCooldown = 1;
        private int _frameCoolDown = 1;

        private readonly List<int> _pressedPads;
        private readonly SmoothColorTransition _solidColorTransition;
        private readonly SmoothColorTransition _variableColorTransition;

        bool _specialModesEnabled = true;

        bool _disposed = false;

        #region Color Palettes
        private static Color[] Red = new Color[]
            {
                Color.FromArgb(Convert.ToInt32("0xFFFF0000", 16)), // Red
                Color.FromArgb(Convert.ToInt32("0xFFFE1600", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFF1000", 16)), 
                Color.FromArgb(Convert.ToInt32("0xFFFE0800", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFF1400", 16)), 
                Color.FromArgb(Convert.ToInt32("0xFFFE2000", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFE2700", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFF2700", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFE1500", 16)),
            };

        private static Color[] Orange = new Color[]
        {
                Color.FromArgb(Convert.ToInt32("0xFFFE5400", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFE3100", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFF6800", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFF2600", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFEE4600", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFEE2100", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFE4000", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFF3200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFF5400", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFE2600", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFE4200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFD6600", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFF3600", 16)),
        };

        private static Color[] OrangeYellow = new Color[]
        {
                Color.FromArgb(Convert.ToInt32("0xFFFD6600", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFE5400", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFF6800", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFEE4600", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFE4000", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFF5400", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFE4200", 16)),
        };

        private static Color[] Yellow = new Color[]
        {
                Color.FromArgb(Convert.ToInt32("0xFFFC8200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFC6200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFC7200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFA7200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFC5400", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFC5100", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFA5200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFA7300", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFC6100", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFA6800", 16)),
        };

        private static Color[] YellowGreen = new Color[]
        {
                Color.FromArgb(Convert.ToInt32("0xFFBCB200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFBC8200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFBCA200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFBAA200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFBC8400", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFBC8100", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFBA8200", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFBAA300", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFBC9100", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFBA9800", 16)),
        };

        private static Color[] Green = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xFF00FF00", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF78FF15", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF68FF30", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF00DF00", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF54FF20", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF42FF21", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF00EF00", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF36FF13", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF34EF24", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF48FE21", 16)),
        };

        private static Color[] GreenBlue = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xFF00FF30", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF78FF45", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF68FF60", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF00DF30", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF54FF50", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF42FF51", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF00EF30", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF36FF43", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF34EF54", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF48FE51", 16)),
        };

        private static Color[] Blue = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xFF0000FF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF00C0FF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF00D6FF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF15C0FF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF00C0FF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF0000EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF25C0EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF12D6F4", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF23D6E6", 16)),
        };

        private static Color[] DeepBlue = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xFF0000FF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF1220FF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF0066FF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF0940FF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF0060FF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF0800EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF0626EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF1481F4", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF1282E6", 16)),
        };

        private static Color[] BlueViolet = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xFFE000DF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFE000EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFE020EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFE026EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF510EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFE030EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFE000DF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF520DF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFE236E4", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF346D6", 16)),
        };

        private static Color[] Violet = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xFFFF00DF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF15DF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF15EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF015EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF08DF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF608DF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF00EF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF508E5", 16)),
        };

        private static Color[] VioletRed = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xFFFF009F", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF159F", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF15CF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF015CF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF089F", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF6089F", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF00BF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF508B5", 16)),
        };

        private static Color[] ExtraVioletRed = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xFFFF004F", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF154F", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF159F", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF0159F", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF084F", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF6084F", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFF00AF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF508A5", 16)),
        };

        private Color[][] Palettes = new Color[][]
        {
            Red,
            Orange,
            OrangeYellow,
            Yellow,
            YellowGreen,
            Green,
            GreenBlue,
            Blue,
            DeepBlue,
            BlueViolet,
            Violet,
            VioletRed,
            ExtraVioletRed
        };

        private int _paletteIndex = 0;
        #endregion

        public ColorPaletteAnimation(
            int pixelCount,
            IAudioService audioService,
            CancellationToken cancellationToken)
        {
            _rootFrame = new Pixel[pixelCount];

            _currentAnimationColors = new Color[pixelCount];
            _targetAnimationColors = new Color[Math.Max(pixelCount, Palettes.Max(p => p.Length))];
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
                _currentAnimationColors[i] = Palettes[0][j];
                _sourceAnimationColors[i] = Palettes[0][j];
                j = (j + 1) % Palettes[0].Length;
            }
            j = 1;
            for (int i = 0; i < _targetAnimationColors.Length; i++)
            {
                _targetAnimationColors[i] = Palettes[0][j];
                j = (j + 1) % Palettes[0].Length;
            }

            _cancellationToken = cancellationToken;
        }

        ~ColorPaletteAnimation()
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
                _automationTimer.Change(10, Timeout.Infinite);
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
                        for (int i = 0; i < 30; i++)
                        {
                            // Should target be _targetAnimationColors?
                            if (_solidColorTransition.Transition(_sourceAnimationColors, _solidColorFrame, _currentAnimationColors))
                            {
                                Array.Copy(_solidColorFrame, _sourceAnimationColors, _solidColorFrame.Length);
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
            navigationColorIndex = (navigationColorIndex + 1) % Palettes.Length;
            SetNavigationColor(Palettes[navigationColorIndex][0]);
        }

        private void StartTimers()
        {
            _automationTimer.Change(10, Timeout.Infinite);
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
                    _automationTimer.Change(10, Timeout.Infinite);
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
                padStates[i] = new PadState(PadIllumination.Solid, Palettes[i % Palettes.Length][0]);
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
                        if (e.CurrentLevel >= -40.0d || e.AverageLevel > -35.0d)
                        {
                            var level = Convert.ToInt32(Math.Min(Math.Max(Math.Round(e.CurrentLevel / e.AverageLevel) * 5, 1), 5));
                            for (int i = 0; i < level; i++)
                            {
                                Animate(forward: true, fromInput: true);
                            }
                            _frameCoolDown = DefaultFrameCooldown;
                        }
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
                        SetTargetPalette(padNumber);

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
                            SetTargetPalette(_pressedPads.Last());
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
                _solidColorTransition.Transition(_sourceAnimationColors, _solidColorFrame, _currentAnimationColors, recomputeDistance: true);
            }
        }

        private void SetTargetPalette(int paletteIndex)
        {
            lock (_rootFrame)
            {
                _paletteIndex = paletteIndex % Palettes.Length;

                var targetPalette = Palettes[_paletteIndex];
                Array.Copy(_currentAnimationColors, _sourceAnimationColors, _currentAnimationColors.Length);
                int j = 0;
                for (int i = 0; i < _targetAnimationColors.Length; i++)
                {
                    _targetAnimationColors[i] = targetPalette[j];
                    j = (j + 1) % targetPalette.Length;
                }
                _variableColorTransition.Transition(_sourceAnimationColors, _targetAnimationColors, _currentAnimationColors, recomputeDistance: true);
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
