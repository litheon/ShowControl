using ArtNetSharp.Communication;
using ArtNetSharp;
using ShowControl.Animation;
using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
using ShowControl.Devices.Input;
using ShowControl.Devices.Input.Extensions;
using ShowControl.Devices.Output;
using ShowControl.Graphics;
using ShowControl.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ShowControl.Rendering
{
    public class RenderService : IRenderService
    {
        private enum NavigationLevel { Root, KeyAnimation, TransformAnimation};
        private IDmxOutputDevice[] _outputDevices;
        private readonly Thread _renderThread;
        private readonly CancellationTokenSource _cts;
        private readonly TaskCompletionSource<bool> _tcs;
        private readonly IDmxDeviceEnumerator _deviceEnumerator;
        private readonly IInputService _inputService;
        private readonly IAudioService _audioService;
        private readonly IGraphicsHost _graphicsHost;
        private readonly IDmxDevice[] _devices;

        private readonly IRenderSurface[] _renderSurfaces;
        private readonly IRenderSurface _pixelChannels;
        private readonly IRenderSurface _parChannels;

        private readonly object _navigationLock = new object();
        private IMidiInputDevice _inputDevice;
        private NavigationLevel _navigationLevel;
        private int _activeKeyAnimation = -1;
        private IKeyAnimation[] _keyAnimations;
        private IKeyAnimation[] _auxilaryKeyAnimations;
        private int _activeTransformAnimation = -1;
        private ITransformAnimation[] _transformAnimations;
        private PadState[] _padStates;
        private int _padTransitionStartIndex;

        private int _masterFader = 15;
        private int _auxilaryMasterFader = 251;

        private Timer NavButtonBlinkTimer;
        private Timer RenderOperatatorTimer;
        private readonly TimeSpan RenderDelayTime = TimeSpan.FromMinutes(10);
        private double _lastAverageLevel;

        private static Button[] _blinkButtons = new Button[]
        {
            Button.Zoom, Button.Select, Button.Right, Button.Left, Button.Down
        };
        private static int _blinkIndex = 0;

        private readonly ILogger<RenderService> _logger;
        private readonly IArtNetController _artNetController;
        private Stopwatch _frameRateStopWatch = new Stopwatch();

        private static void Blink(object state)
        {
            var service = (RenderService)state;
            var illumination = service._inputDevice.GetButtonIllumination(Button.Up);
            var upIllumination = illumination == LightState.On ? LightState.Off : LightState.On;
            List<ButtonState> buttonStates = new List<ButtonState>();
            buttonStates.Add(new ButtonState(Button.Up, upIllumination));
            for (int i = 0; i < _blinkButtons.Length; i++)
            {
                buttonStates.Add(new ButtonState(_blinkButtons[i], i == _blinkIndex ? LightState.On : LightState.Off));
            }
            _blinkIndex = (_blinkIndex + 1) % _blinkButtons.Length;
            service._inputDevice.SetButtonIlluminations(buttonStates.ToArray());
        }

        private void ChangeAnimation(object state)
        {
            lock(_navigationLock)
            {
                if (_activeKeyAnimation == -1 && _keyAnimations.Length > 0)
                {
                    if (_activeTransformAnimation != -1)
                    {
                        SetActiveTransformAnimation(_activeTransformAnimation);
                    }
                    SetActiveKeyAnimation(0);
                    SetNavigationLevel(NavigationLevel.KeyAnimation);
                }
                else if (_activeTransformAnimation == -1 && _transformAnimations.Length > 0)
                {
                    SetActiveTransformAnimation(0);
                }
                else if (_activeTransformAnimation < _transformAnimations.Length - 2)
                {
                    SetActiveTransformAnimation(_activeTransformAnimation + 1);
                }
                else if (_activeKeyAnimation <  _keyAnimations.Length - 2)
                {
                    if (_activeTransformAnimation != -1)
                    {
                        SetActiveTransformAnimation(_activeTransformAnimation);
                    }
                    SetActiveKeyAnimation(_activeKeyAnimation + 1);
                    SetNavigationLevel(NavigationLevel.KeyAnimation);
                }
                else if (_keyAnimations.Length > 0 && _activeKeyAnimation == _keyAnimations.Length - 1)
                {
                    if (_activeTransformAnimation != -1)
                    {
                        SetActiveTransformAnimation(_activeTransformAnimation);
                    }
                    SetActiveKeyAnimation(0);
                    SetNavigationLevel(NavigationLevel.KeyAnimation);
                }
                else if (_activeTransformAnimation != -1)
                {
                    SetActiveTransformAnimation(_activeTransformAnimation);
                }
                else
                {
                    SetActiveKeyAnimation(0);
                    SetNavigationLevel(NavigationLevel.KeyAnimation);
                }

                if (_activeKeyAnimation != -1)
                {
                    var activeKeyAnimation = _keyAnimations[_activeKeyAnimation];
                    var modeEnabledAnimation = activeKeyAnimation as IModeEnabledAnimation;
                    if (modeEnabledAnimation != null)
                    {
                        if (_lastAverageLevel > -60 && _lastAverageLevel != 0)
                        {
                            modeEnabledAnimation.SetMode(AnimationMode.SoundReactive);
                        }
                        else
                        {
                            modeEnabledAnimation.SetMode(AnimationMode.Automatic);
                        }
                    }
                }
            }
        }

        public RenderService(
            IDmxDeviceEnumerator deviceEnumerator,
            IInputService inputService,
            IGraphicsHost graphicsHost,
            IAudioService audioService,
            ILogger<RenderService> logger,
            IArtNetController artNetController
            )
        {
            _logger = logger;
            _artNetController = artNetController;
            _deviceEnumerator = deviceEnumerator;
            _inputService = inputService;
            _audioService = audioService;
            _graphicsHost = graphicsHost;
            _audioService.AudioFrameProcessed += _audioService_AudioFrameProcessed;
            _renderThread = new Thread(RenderThreadLoop);
            _tcs = new TaskCompletionSource<bool>();
            _cts = new CancellationTokenSource();

            _pixelChannels = RenderSurface.Create(
                    new RenderSettings(255),
                    Enumerable.Repeat(WS2812B.Instance, 3720).ToArray()
            );
            _parChannels = RenderSurface.Create(
                    new RenderSettings(255),
                    Sheds.Instance
                    //Sheds.Instance,
                    //Sheds.Instance,
                    //Sheds.Instance,
                    //Sheds.Instance,
                    //Sheds.Instance,
                    //Sheds.Instance,
                    //Sheds.Instance,
                    //Sheds.Instance,
                    //Sheds.Instance,
                    //Sheds.Instance,
                    //Sheds.Instance
            );
            _renderSurfaces = new IRenderSurface[]
            {
                 RenderSurface.Create(
                    new RenderSettings(255),
                    _pixelChannels,
                    _parChannels
                ),
            };

            _devices = _renderSurfaces.SelectMany(r => r.GetDevices()).ToArray();
        }

        private void _audioService_AudioFrameProcessed(object sender, AudioFrameProcessedEventArgs e)
        {
            _lastAverageLevel = e.AverageLevel;
        }

        public RenderService(
            IDmxDeviceEnumerator deviceEnumerator,
            IInputService inputService,
            IAudioService audioService,
            IGraphicsHost graphicsHost,
            Action<IDmxDevice[]> dmxDeviceAction,
            ILogger<RenderService> logger,
            IArtNetController artNetController
            ) : this(deviceEnumerator, inputService, graphicsHost, audioService, logger, artNetController)
        {
            dmxDeviceAction(_devices);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("Test");
            System.Diagnostics.Debug.WriteLine("Init");
            _inputService.OnInputReceived += InputService_OnInputReceived;
            _inputService.OnInputDeviceReconnected += _inputService_OnInputDeviceReconnected;
            System.Diagnostics.Debug.WriteLine("Input service");
            Console.WriteLine("Input service");
            _inputDevice = _inputService.Devices.FirstOrDefault();
            Console.WriteLine("Input Device null: " + (_inputDevice == null ? "True" : "False"));
            System.Diagnostics.Debug.WriteLine("Input Device null: " + _inputDevice == null ? "True" : "False");
            _padTransitionStartIndex = _inputDevice.PadCount / 2;
            Console.WriteLine("_deviceEnumerator null: " + (_deviceEnumerator == null ? "True" : "False"));
            Console.WriteLine("_cts null: " + (_cts == null ? "True" : "False"));
            _outputDevices = _deviceEnumerator.GetDmxDevices(_cts.Token);
            _renderThread.Start();
            Initialize();
            NavButtonBlinkTimer = new Timer(Blink, this, 500, 500);
            //RenderOperatatorTimer = new Timer(ChangeAnimation, this, RenderDelayTime, RenderDelayTime);
            return Task.CompletedTask;
        }

        private void _inputService_OnInputDeviceReconnected(object sender, EventArgs e)
        {
            lock (_navigationLock)
            {
                SetNavigationLevel(_navigationLevel);
            }
        }

        private bool SetActiveKeyAnimation(int index)
        {
            IKeyAnimation animation;
            lock (_navigationLock)
            {
                if (index >= _keyAnimations.Length)
                {
                    return false;
                }
                if (_activeKeyAnimation > -1)
                {
                    _padStates[_activeKeyAnimation].PadIllumination = PadIllumination.Solid;
                    var existingAnimation = _keyAnimations[_activeKeyAnimation];
                    existingAnimation.Stop();
                    existingAnimation = _auxilaryKeyAnimations[_activeKeyAnimation];
                    existingAnimation.Stop();
                }
                _padStates[index].PadIllumination = PadIllumination.Blink;
                _activeKeyAnimation = index;
                animation = _keyAnimations[_activeKeyAnimation];
            }
            return true;
        }

        private bool SetActiveTransformAnimation(int index)
        {
            ITransformAnimation animation;
            lock (_navigationLock)
            {
                if (index >= _transformAnimations.Length)
                {
                    return false;
                }
                if (_activeTransformAnimation == index)
                {
                    _padStates[index + _padTransitionStartIndex].PadIllumination = PadIllumination.Solid;
                    animation = _transformAnimations[_activeTransformAnimation];
                    animation.Stop();
                    _activeTransformAnimation = -1;
                    return false;
                }
                else if (_activeTransformAnimation > -1)
                {
                    _padStates[_activeTransformAnimation + _padTransitionStartIndex].PadIllumination = PadIllumination.Solid;
                    var existingAnimation = _transformAnimations[_activeTransformAnimation];
                    existingAnimation.Stop();
                }

                _padStates[index + _padTransitionStartIndex].PadIllumination = PadIllumination.Breathe;
                _activeTransformAnimation = index;
                animation = _transformAnimations[_activeTransformAnimation];
                animation.Start(_inputDevice.AsReadOnly());
            }
            return false;
        }

        private void SetNavigationLevel(NavigationLevel navigationLevel)
        {
            IAnimation animation = null;
            IAnimation auxilaryAnimation = null;
            lock (_navigationLock)
            {
                _navigationLevel = navigationLevel;

                if (navigationLevel == NavigationLevel.Root)
                {
                    _inputDevice.ClearInterface();
                    _inputDevice.SetButtonIllumination(Button.Up, LightState.On);
                    _inputDevice.SetPadStates(_padStates);
                }
                else if (navigationLevel == NavigationLevel.KeyAnimation && _activeKeyAnimation > -1)
                {
                    animation = _keyAnimations[_activeKeyAnimation];
                    auxilaryAnimation = _auxilaryKeyAnimations[_activeKeyAnimation];
                }
                else if (navigationLevel == NavigationLevel.TransformAnimation && _activeTransformAnimation > -1)
                {
                    animation = _transformAnimations[_activeTransformAnimation];
                }
            }

            var deviceInitializationState = animation?.Start(_inputDevice.AsReadOnly());
            auxilaryAnimation?.Start(_inputDevice.AsReadOnly());
            if (deviceInitializationState?.PadStates != null && deviceInitializationState.PadStates.Length > 0)
            {
                _inputDevice.SetPadStates(deviceInitializationState.PadStates);
            }
            if (deviceInitializationState?.ButtonLightStates != null)
            {
                foreach (var buttonLightState in deviceInitializationState.ButtonLightStates)
                {
                    _inputDevice.SetButtonIllumination(buttonLightState.Button, buttonLightState.LightState);
                }
            }
        }

        private void InputService_OnInputReceived(object sender, DeviceInputEventArgs e)
        {
            NavigationLevel navigationLevel;
            lock (_navigationLock)
            {
                navigationLevel = _navigationLevel;
            }
            if (e.EventType == DeviceInputEventType.Navigation)
            {
                ProcessNavigationButtonPressed(e.ButtonDirection, e.NavigationDirection);
            }
            else if (navigationLevel == NavigationLevel.Root)
            {
                if (e.EventType == DeviceInputEventType.Pad)
                {
                    ProcessPadPressed(e.ButtonDirection, e.PadNumber.Value);
                }
                else if (e.EventType == DeviceInputEventType.RotaryEncoder && e.RotaryNumber == 1)
                {
                    var direction = e.RotaryDirection;
                    if (direction == RotaryDirection.Up)
                    {
                        _masterFader.InterlockedDecrement(0, byte.MaxValue);
                    }
                    else if (direction == RotaryDirection.Down)
                    {
                        _masterFader.InterlockedIncrement(0, byte.MaxValue);
                    }
                }
                else if (e.EventType == DeviceInputEventType.RotaryEncoder && e.RotaryNumber == 2)
                {
                    var direction = e.RotaryDirection;
                    if (direction == RotaryDirection.Up)
                    {
                        _auxilaryMasterFader.InterlockedDecrement(0, byte.MaxValue);
                    }
                    else if (direction == RotaryDirection.Down)
                    {
                        _auxilaryMasterFader.InterlockedIncrement(0, byte.MaxValue);
                    }
                }
            }
            else
            {
                IAnimation activeKeyAnimation = null;
                IAnimation activeAuxilaryAnimation = null;
                IAnimation activeTransformAnimation = null;
                lock (_navigationLock)
                {
                    if (_navigationLevel == NavigationLevel.KeyAnimation)
                    {
                        activeKeyAnimation = _activeKeyAnimation > -1 ? _keyAnimations[_activeKeyAnimation] : null;
                        activeAuxilaryAnimation = _activeKeyAnimation > -1 ? _auxilaryKeyAnimations[_activeKeyAnimation] : null;
                        activeTransformAnimation = _activeTransformAnimation > -1 ? _transformAnimations[_activeTransformAnimation] : null;
                    }
                }
                var midiAwareKeyAnimation = activeKeyAnimation as MidiAwareAnimation;
                midiAwareKeyAnimation?.MidiAware_OnMessageReceived(sender, e);

                var midiAuxillaryKeyAnimation = activeAuxilaryAnimation as MidiAwareAnimation;
                midiAuxillaryKeyAnimation?.MidiAware_OnMessageReceived(sender, e);

                var midiAwareTransformAnimation = activeTransformAnimation as MidiAwareAnimation;
                midiAwareTransformAnimation?.MidiAware_OnMessageReceived(sender, e);
            }
            //RenderOperatatorTimer.Change(RenderDelayTime, RenderDelayTime);
        }

        private void ProcessPadPressed(ButtonDirection direction, byte padNumber)
        {   
            if (direction == ButtonDirection.Press)
            {
                if (padNumber < _padTransitionStartIndex)
                {
                    if (SetActiveKeyAnimation(padNumber))
                    {
                        _inputDevice.ClearInterface();
                        SetNavigationLevel(NavigationLevel.KeyAnimation);
                    }
                }
                else
                {
                    int transformAnimationIndex = padNumber - _padTransitionStartIndex;
                    if (SetActiveTransformAnimation(transformAnimationIndex))
                    {
                        _inputDevice.ClearInterface();
                        SetNavigationLevel(NavigationLevel.TransformAnimation);
                    }
                }
            }
            else if (direction == ButtonDirection.Release)
            {
                _inputDevice.SetPadStates(_padStates);
            }
        }

        private void ProcessNavigationButtonPressed(ButtonDirection direction, NavigationButton navigationButton)
        {
            if (direction == ButtonDirection.Press && navigationButton == NavigationButton.Up)
            {
                _inputDevice.ClearInterface();
                lock (_navigationLock)
                {
                    SetNavigationLevel(_navigationLevel == NavigationLevel.Root ? NavigationLevel.KeyAnimation : NavigationLevel.Root);
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_activeKeyAnimation > -1)
            {
                _keyAnimations[_activeKeyAnimation].Stop();
                _auxilaryKeyAnimations[_activeKeyAnimation].Stop();
            }
            if (_activeTransformAnimation > -1)
            {
                _transformAnimations[_activeTransformAnimation].Stop();
            }
            
            _cts.Cancel();
            _inputService.Stop();
            _audioService.Dispose();
            foreach (var animation in _keyAnimations)
            {
                var disposableAnimation = animation as IDisposable;
                if (disposableAnimation != null)
                {
                    disposableAnimation.Dispose();
                }    
            }
            foreach (var animation in _auxilaryKeyAnimations)
            {
                var disposableAnimation = animation as IDisposable;
                if (disposableAnimation != null)
                {
                    disposableAnimation.Dispose();
                }
            }
            return _tcs.Task;
        }

        private void Initialize()
        {
            int address = 1;
            for (int j = 0; j < _devices.Length; j++)
            {
                System.Diagnostics.Debug.WriteLine("Address {0} for {1}: {2}", j + 1, _devices[j].GetType().ToString(), address);
                Console.WriteLine("Address {0} for {1}: {2}", j + 1, _devices[j].GetType().ToString(), address);
                address += _devices[j].ChannelCount;
            }

            var roygbiv = new Color[6]
            {
                    Color.FromArgb(0xFF, 0xFF, 0x00, 0x00),
                    Color.FromArgb(0xFF, 0xFF, 0xA5, 0x00),
                    Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00),
                    Color.FromArgb(0xFF, 0x00, 0xFF, 0x00),
                    Color.FromArgb(0xFF, 0x00, 0x00, 0xFF),
                    Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)
                    //Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            };

            var warmColdRoygviv = new Color[]
            {
                Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0xFE, Color.DarkRed),
                Color.FromArgb(0xFD, Color.OrangeRed),

                Color.FromArgb(0xE6, 0xFF, 0xA5, 0x00),
                Color.FromArgb(0xF3, Color.DarkOrange),

                Color.FromArgb(0xC7, 0xFF, 0xFF, 0x00),
                Color.FromArgb(0xF9, Color.Gold),
                Color.FromArgb(0xB6, Color.DarkGoldenrod),

                Color.FromArgb(0xB8, Color.Green),
                Color.FromArgb(0xF8, Color.SeaGreen),

                Color.FromArgb(0xA1, 0x00, 0x00, 0xFF),

                Color.FromArgb(0x95, 0xFF, 0x00, 0xFF),
                Color.FromArgb(0xD8, Color.Purple),


                Color.FromArgb(0xF9, Color.Red),
                Color.FromArgb(0x26, Color.DarkRed),
                Color.FromArgb(0xA9, Color.OrangeRed),
                Color.FromArgb(0x86, Color.Gold),
                Color.FromArgb(0x92, Color.Red),
                Color.FromArgb(0xCD, Color.DarkRed),
                Color.FromArgb(0x20, Color.OrangeRed),
                Color.FromArgb(0xE0, Color.Gold),
                Color.FromArgb(0x50, Color.Red),
                Color.FromArgb(0x10, Color.DarkRed),
                Color.FromArgb(0xC8, Color.OrangeRed),
                Color.FromArgb(0x03, Color.Gold),
                Color.FromArgb(0x82, Color.Red),

                Color.FromArgb(0xC2, Color.DarkRed),
                Color.FromArgb(0x11, Color.OrangeRed),
                Color.FromArgb(0xDB, Color.Gold),
                Color.FromArgb(0x70, Color.Red),
                Color.FromArgb(0x83, Color.DarkRed),
                Color.FromArgb(0x2A, Color.OrangeRed),
                Color.FromArgb(0xD5, Color.Gold),



                Color.FromArgb(0x70, Color.OrangeRed),
                Color.FromArgb(0x83, Color.Orange),
                Color.FromArgb(0x2A, Color.DarkOrange),
                Color.FromArgb(0xD5, Color.OrangeRed),
                Color.FromArgb(0xEF, Color.Orange),
                Color.FromArgb(0x64, Color.DarkOrange),
                Color.FromArgb(0x2F, Color.OrangeRed),
                Color.FromArgb(0x75, Color.Orange),
                Color.FromArgb(0xC7, Color.DarkOrange),
                Color.FromArgb(0xD2, Color.OrangeRed),
                Color.FromArgb(0x9E, Color.Orange),
                Color.FromArgb(0x34, Color.DarkOrange),
                Color.FromArgb(0x9E, Color.OrangeRed),
                Color.FromArgb(0x34, Color.Orange),
                Color.FromArgb(0xBD, Color.DarkOrange),

                Color.FromArgb(0x1A, Color.Gold),
                Color.FromArgb(0xE4, Color.Goldenrod),
                Color.FromArgb(0x3D, Color.DarkGoldenrod),
                Color.FromArgb(0xCE, Color.LightGoldenrodYellow),
                Color.FromArgb(0xA8, Color.PaleGoldenrod),
                Color.FromArgb(0x6B, Color.Gold),
                Color.FromArgb(0x8D, Color.Goldenrod),
                Color.FromArgb(0xDD, Color.DarkGoldenrod),
                Color.FromArgb(0xB6, Color.LightGoldenrodYellow),
                Color.FromArgb(0x7B, Color.PaleGoldenrod),
                Color.FromArgb(0xD3, Color.DarkGoldenrod),
                Color.FromArgb(0x36, Color.LightGoldenrodYellow),
                Color.FromArgb(0x12, Color.PaleGoldenrod),
                Color.FromArgb(0xDC, Color.LightGoldenrodYellow),
                Color.FromArgb(0x6B, Color.PaleGoldenrod),

                //Color.Blue,
                //Color.CornflowerBlue,
                //Color.LightBlue,
                //Color.DarkBlue,
                //Color.DeepSkyBlue,
                //Color.DarkSlateBlue,
                //Color.RoyalBlue,
                //Color.DeepSkyBlue,
                //Color.DarkSlateBlue,
                //Color.RoyalBlue,
                //Color.Blue,

                Color.FromArgb(0x23, Color.BlueViolet),
                Color.FromArgb(0x05, Color.Purple),
                Color.FromArgb(0x63, Color.DarkViolet),
                Color.FromArgb(0xB4, Color.BlueViolet),
                Color.FromArgb(0x17, Color.Purple),
                Color.FromArgb(0xCD, Color.DarkViolet),
                Color.FromArgb(0x1D, Color.MediumVioletRed),
                Color.FromArgb(0x71, Color.PaleVioletRed),
                Color.FromArgb(0x3D, Color.MediumVioletRed),
                Color.FromArgb(0x3D, Color.PaleVioletRed),
                Color.FromArgb(0x4D, Color.Purple),
                Color.FromArgb(0xF2, Color.DarkViolet),
                Color.FromArgb(0xD5, Color.MediumVioletRed),

                Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            };

            var frequentWarmColdRoygviv = new Color[]
            {
                Color.FromArgb(0xFF, Color.Red),
                Color.FromArgb(0XFD, Color.Orange),

                Color.FromArgb(0xC7, Color.Yellow),
                Color.FromArgb(0xF9, Color.Gold),
                Color.FromArgb(0xB6, Color.DarkGoldenrod),

                Color.FromArgb(0xB8, Color.Green),
                Color.FromArgb(0xF8, Color.SeaGreen),

                Color.LightBlue,
                Color.FromArgb(0xA1, 0x00, 0x00, 0xFF),

                Color.FromArgb(0x95, 0xFF, 0x00, 0xFF),
                Color.FromArgb(0xD8, Color.Purple),
            };

            var warm = new Color[]
            {
 
                Color.FromArgb(0xFF, Color.OrangeRed),
                Color.FromArgb(0xFF, Color.DarkOrange),

                Color.FromArgb(0xFF, Color.Gold),
                Color.FromArgb(0xFF, Color.DarkGoldenrod),


                Color.FromArgb(0x79, Color.Gold),
                Color.FromArgb(0x4F, Color.Red),
                Color.FromArgb(0xE1, Color.OrangeRed),
                Color.FromArgb(0xDD, Color.Gold),
                Color.FromArgb(0x35, Color.OrangeRed),
                Color.FromArgb(0x75, Color.Gold),
                Color.FromArgb(0x57, Color.PaleGoldenrod),
                Color.FromArgb(0x49, Color.DarkGoldenrod),
                Color.FromArgb(0xE3, Color.LightGoldenrodYellow),
                Color.FromArgb(0xE4, Color.OrangeRed),
                Color.FromArgb(0x4C, Color.Gold),
                Color.FromArgb(0xDC, Color.OrangeRed),
                Color.FromArgb(0x2F, Color.Gold),



                Color.FromArgb(0x79, Color.OrangeRed),
                Color.FromArgb(0x4F, Color.Orange),
                Color.FromArgb(0xE1, Color.DarkOrange),
                Color.FromArgb(0xDD, Color.OrangeRed),
                Color.FromArgb(0x35, Color.Orange),
                Color.FromArgb(0x75, Color.DarkOrange),
                Color.FromArgb(0x57, Color.OrangeRed),
                Color.FromArgb(0x49, Color.Orange),
                Color.FromArgb(0xE3, Color.DarkOrange),
                Color.FromArgb(0xE4, Color.OrangeRed),
                Color.FromArgb(0x4C, Color.Goldenrod),
                Color.FromArgb(0xDC, Color.DarkGoldenrod),
                Color.FromArgb(0x2F, Color.LightGoldenrodYellow),
                Color.FromArgb(0xAA, Color.Orange),
                Color.FromArgb(0x65, Color.DarkOrange),
                Color.FromArgb(0x8E, Color.OrangeRed),
                Color.FromArgb(0xA1, Color.Orange),
                Color.FromArgb(0xA8, Color.PaleGoldenrod),
                Color.FromArgb(0x12, Color.LightGoldenrodYellow),
                Color.FromArgb(0xD3, Color.PaleGoldenrod),
                Color.FromArgb(0x71, Color.DarkOrange),

                Color.FromArgb(0xf9, Color.Gold),

                Color.FromArgb(0x8F, Color.PaleGoldenrod),
                Color.FromArgb(0x25, Color.Gold),
                Color.FromArgb(0x40, Color.Goldenrod),
                Color.FromArgb(0x11, Color.DarkGoldenrod),
                Color.FromArgb(0x62, Color.LightGoldenrodYellow),

            };

            var warmLucidas = new Color[]
            {

                Color.FromArgb(0xFF, Color.OrangeRed),
                Color.FromArgb(0xFF, Color.DarkOrange),
                Color.FromArgb(0xFF, Color.Gold),
                Color.FromArgb(0xFF, Color.DarkGoldenrod),
                Color.FromArgb(0x79, Color.Gold),
                Color.FromArgb(0x4F, Color.Red),
                Color.FromArgb(0xE1, Color.OrangeRed),
                Color.FromArgb(0xDD, Color.Gold),

                Color.FromArgb(0xFF, Color.OrangeRed),
                Color.FromArgb(0xFF, Color.DarkOrange),
                Color.FromArgb(0xFF, Color.Gold),
                Color.FromArgb(0xFF, Color.DarkGoldenrod),
                Color.FromArgb(0x79, Color.Gold),
                Color.FromArgb(0x4F, Color.Red),
                Color.FromArgb(0xE1, Color.OrangeRed),
                Color.FromArgb(0xDD, Color.Gold),

                Color.FromArgb(0xFF, Color.OrangeRed),
                Color.FromArgb(0xFF, Color.DarkOrange),
                Color.FromArgb(0xFF, Color.Gold),
                Color.FromArgb(0xFF, Color.DarkGoldenrod),
                Color.FromArgb(0x79, Color.Gold),
                Color.FromArgb(0x4F, Color.Red),
                Color.FromArgb(0xE1, Color.OrangeRed),
                Color.FromArgb(0xDD, Color.Gold),

                Color.FromArgb(0xFF, Color.OrangeRed),
                Color.FromArgb(0xFF, Color.DarkOrange),
                Color.FromArgb(0xFF, Color.Gold),
                Color.FromArgb(0xFF, Color.DarkGoldenrod),
                Color.FromArgb(0x79, Color.Gold),
                Color.FromArgb(0x4F, Color.Red),
                Color.FromArgb(0xE1, Color.OrangeRed),
                Color.FromArgb(0xDD, Color.Gold),

                Color.FromArgb(0x35, Color.OrangeRed),
                Color.FromArgb(0x75, Color.Gold),
                Color.FromArgb(0x57, Color.PaleGoldenrod),
                Color.FromArgb(0x49, Color.DarkGoldenrod),
                Color.FromArgb(0xE4, Color.OrangeRed),
                Color.FromArgb(0x4C, Color.Gold),
                Color.FromArgb(0xDC, Color.OrangeRed),
                Color.FromArgb(0x2F, Color.Gold),

                Color.FromArgb(0x35, Color.OrangeRed),
                Color.FromArgb(0x75, Color.Gold),
                Color.FromArgb(0x57, Color.PaleGoldenrod),
                Color.FromArgb(0x49, Color.DarkGoldenrod),
                Color.FromArgb(0xE4, Color.OrangeRed),
                Color.FromArgb(0x4C, Color.Gold),
                Color.FromArgb(0xDC, Color.OrangeRed),

                Color.FromArgb(0x35, Color.OrangeRed),
                Color.FromArgb(0x75, Color.Gold),
                Color.FromArgb(0x57, Color.PaleGoldenrod),
                Color.FromArgb(0x49, Color.DarkGoldenrod),
                Color.FromArgb(0xE4, Color.OrangeRed),
                Color.FromArgb(0x4C, Color.Gold),
                Color.FromArgb(0xDC, Color.OrangeRed),

                Color.FromArgb(0x35, Color.OrangeRed),
                Color.FromArgb(0x75, Color.Gold),
                Color.FromArgb(0x57, Color.PaleGoldenrod),
                Color.FromArgb(0x49, Color.DarkGoldenrod),
                Color.FromArgb(0xE4, Color.OrangeRed),
                Color.FromArgb(0x4C, Color.Gold),
                Color.FromArgb(0xDC, Color.OrangeRed),

                Color.FromArgb(0x79, Color.OrangeRed),
                Color.FromArgb(0x4F, Color.Orange),
                Color.FromArgb(0xE1, Color.DarkOrange),
                Color.FromArgb(0xDD, Color.OrangeRed),
                Color.FromArgb(0x35, Color.Orange),
                Color.FromArgb(0x75, Color.DarkOrange),
                Color.FromArgb(0x57, Color.OrangeRed),
                Color.FromArgb(0x49, Color.Orange),

                Color.FromArgb(0x79, Color.OrangeRed),
                Color.FromArgb(0x4F, Color.Orange),
                Color.FromArgb(0xE1, Color.DarkOrange),
                Color.FromArgb(0xDD, Color.OrangeRed),
                Color.FromArgb(0x35, Color.Orange),
                Color.FromArgb(0x75, Color.DarkOrange),
                Color.FromArgb(0x57, Color.OrangeRed),
                Color.FromArgb(0x49, Color.Orange),

                Color.FromArgb(0x79, Color.OrangeRed),
                Color.FromArgb(0x4F, Color.Orange),
                Color.FromArgb(0xE1, Color.DarkOrange),
                Color.FromArgb(0xDD, Color.OrangeRed),
                Color.FromArgb(0x35, Color.Orange),
                Color.FromArgb(0x75, Color.DarkOrange),
                Color.FromArgb(0x57, Color.OrangeRed),
                Color.FromArgb(0x49, Color.Orange),

                Color.FromArgb(0x79, Color.OrangeRed),
                Color.FromArgb(0x4F, Color.Orange),
                Color.FromArgb(0xE1, Color.DarkOrange),
                Color.FromArgb(0xDD, Color.OrangeRed),
                Color.FromArgb(0x35, Color.Orange),
                Color.FromArgb(0x75, Color.DarkOrange),
                Color.FromArgb(0x57, Color.OrangeRed),
                Color.FromArgb(0x49, Color.Orange),

                Color.FromArgb(0xE3, Color.DarkOrange),
                Color.FromArgb(0xE4, Color.OrangeRed),
                Color.FromArgb(0x4C, Color.Goldenrod),
                Color.FromArgb(0xDC, Color.DarkGoldenrod),
                Color.FromArgb(0x2F, Color.LightGoldenrodYellow),
                Color.FromArgb(0xAA, Color.Orange),
                Color.FromArgb(0x65, Color.DarkOrange),
                Color.FromArgb(0x8E, Color.OrangeRed),

                Color.FromArgb(0xE3, Color.DarkOrange),
                Color.FromArgb(0xE4, Color.OrangeRed),
                Color.FromArgb(0x4C, Color.Goldenrod),
                Color.FromArgb(0xDC, Color.DarkGoldenrod),
                Color.FromArgb(0x2F, Color.LightGoldenrodYellow),
                Color.FromArgb(0xAA, Color.Orange),
                Color.FromArgb(0x65, Color.DarkOrange),
                Color.FromArgb(0x8E, Color.OrangeRed),

                Color.FromArgb(0xE3, Color.DarkOrange),
                Color.FromArgb(0xE4, Color.OrangeRed),
                Color.FromArgb(0x4C, Color.Goldenrod),
                Color.FromArgb(0xDC, Color.DarkGoldenrod),
                Color.FromArgb(0x2F, Color.LightGoldenrodYellow),
                Color.FromArgb(0xAA, Color.Orange),
                Color.FromArgb(0x65, Color.DarkOrange),
                Color.FromArgb(0x8E, Color.OrangeRed),

                Color.FromArgb(0xE3, Color.DarkOrange),
                Color.FromArgb(0xE4, Color.OrangeRed),
                Color.FromArgb(0x4C, Color.Goldenrod),
                Color.FromArgb(0xDC, Color.DarkGoldenrod),
                Color.FromArgb(0x2F, Color.LightGoldenrodYellow),
                Color.FromArgb(0xAA, Color.Orange),
                Color.FromArgb(0x65, Color.DarkOrange),
                Color.FromArgb(0x8E, Color.OrangeRed),

                Color.FromArgb(0xA1, Color.Orange),
                Color.FromArgb(0xA8, Color.PaleGoldenrod),
                Color.FromArgb(0x12, Color.LightGoldenrodYellow),
                Color.FromArgb(0xD3, Color.PaleGoldenrod),
                Color.FromArgb(0x71, Color.DarkOrange),
                Color.FromArgb(0xf9, Color.Gold),
                Color.FromArgb(0x8F, Color.PaleGoldenrod),
                Color.FromArgb(0x25, Color.Gold),

                Color.FromArgb(0xA1, Color.Orange),
                Color.FromArgb(0xA8, Color.PaleGoldenrod),
                Color.FromArgb(0x12, Color.LightGoldenrodYellow),
                Color.FromArgb(0xD3, Color.PaleGoldenrod),
                Color.FromArgb(0x71, Color.DarkOrange),
                Color.FromArgb(0xf9, Color.Gold),
                Color.FromArgb(0x8F, Color.PaleGoldenrod),
                Color.FromArgb(0x25, Color.Gold),

                Color.FromArgb(0xA1, Color.Orange),
                Color.FromArgb(0xA8, Color.PaleGoldenrod),
                Color.FromArgb(0x12, Color.LightGoldenrodYellow),
                Color.FromArgb(0xD3, Color.PaleGoldenrod),
                Color.FromArgb(0x71, Color.DarkOrange),
                Color.FromArgb(0xf9, Color.Gold),
                Color.FromArgb(0x8F, Color.PaleGoldenrod),
                Color.FromArgb(0x25, Color.Gold),

                Color.FromArgb(0xA1, Color.Orange),
                Color.FromArgb(0xA8, Color.PaleGoldenrod),
                Color.FromArgb(0x12, Color.LightGoldenrodYellow),
                Color.FromArgb(0xD3, Color.PaleGoldenrod),
                Color.FromArgb(0x71, Color.DarkOrange),
                Color.FromArgb(0xf9, Color.Gold),
                Color.FromArgb(0x8F, Color.PaleGoldenrod),
                Color.FromArgb(0x25, Color.Gold),

                Color.FromArgb(0xA1, Color.Orange),
                Color.FromArgb(0xA8, Color.PaleGoldenrod),
                Color.FromArgb(0x12, Color.LightGoldenrodYellow),
                Color.FromArgb(0xD3, Color.PaleGoldenrod),
                Color.FromArgb(0x71, Color.DarkOrange),
                Color.FromArgb(0xf9, Color.Gold),
                Color.FromArgb(0x8F, Color.PaleGoldenrod),
                Color.FromArgb(0x25, Color.Gold),
            };

            var space = new Color[]
            {
                Color.FromArgb(Convert.ToInt32("0x15E91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0x1DA52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xDB5D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0xFEFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xC7FF4DE9", 16)), // Pink
                Color.FromArgb(Convert.ToInt32("0x7828C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x7BF5EC00", 16)), // Yellow

                Color.FromArgb(Convert.ToInt32("0x71E91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0x36EF32BF", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xEB28C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xA8FA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x971BC8E2", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xCEC16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x97910E5A", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xFEFF4DE9", 16)), // Pink
                Color.FromArgb(Convert.ToInt32("0xA6F5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0x7EA52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xBC9C236B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x3AFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x048D175B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x57C16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xE2CE2968", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x6E5D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0xDE00E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x96641FA1", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0x5D7033BC", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0x7400E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xC7A52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x98FA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xC5C16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x6B9C236B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x808D175B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x7000E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x2BCE2968", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xCAF5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0xFAF7417A", 16)), // Red-Purple

                Color.FromArgb(Convert.ToInt32("0xE300E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x2200E9FD", 16)), // Blue

                Color.FromArgb(Convert.ToInt32("0x46E91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xC4EF32BF", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xA628C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x2AFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x151BC8E2", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xBBC16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x7E910E5A", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0x2DFF4DE9", 16)), // Pink
                Color.FromArgb(Convert.ToInt32("0xA1F5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0x8CA52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x739C236B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x26FA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x908D175B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x9DC16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x3DCE2968", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x3B5D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0x5300E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x2A641FA1", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0xFD7033BC", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0x7200E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xA7A52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xECFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x4BC16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x4F9C236B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xEC8D175B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x5800E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xE9CE2968", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x9BF5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0xAEF7417A", 16)), // Red-Purple

                Color.FromArgb(Convert.ToInt32("0xCDE91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0x9CEF32BF", 16)),
                Color.FromArgb(Convert.ToInt32("0xB8910E5A", 16)),
                Color.FromArgb(Convert.ToInt32("0x88F13085", 16)),
                Color.FromArgb(Convert.ToInt32("0x35FC2975", 16)),
                Color.FromArgb(Convert.ToInt32("0x71D21283", 16)),

                Color.FromArgb(Convert.ToInt32("0xFEF5EC00", 16)), // Yellow

                Color.FromArgb(Convert.ToInt32("0xFFE91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xFFEF32BF", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF910E5A", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFF13085", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFC2975", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFD21283", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow

                Color.FromArgb(Convert.ToInt32("0x9CE91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xB8EF32BF", 16)),
                Color.FromArgb(Convert.ToInt32("0x88910E5A", 16)),
                Color.FromArgb(Convert.ToInt32("0x35F13085", 16)),
                Color.FromArgb(Convert.ToInt32("0x71FC2975", 16)),
                Color.FromArgb(Convert.ToInt32("0xFED21283", 16)),

                Color.FromArgb(Convert.ToInt32("0xBC28C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x291BC8E2", 16)),
                Color.FromArgb(Convert.ToInt32("0xD002C3E4", 16)),
                Color.FromArgb(Convert.ToInt32("0x1705CBE5", 16)),
                Color.FromArgb(Convert.ToInt32("0x6850FDFC", 16)),
                Color.FromArgb(Convert.ToInt32("0xB802BCDF", 16)),

                Color.FromArgb(Convert.ToInt32("0x25A52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x549C236B", 16)),
                Color.FromArgb(Convert.ToInt32("0xE48D175B", 16)),
                Color.FromArgb(Convert.ToInt32("0x17CE2968", 16)),
                Color.FromArgb(Convert.ToInt32("0x1AF7417A", 16)),
                Color.FromArgb(Convert.ToInt32("0x5300E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x90A52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xB59C236B", 16)),
                Color.FromArgb(Convert.ToInt32("0xF28D175B", 16)),
                Color.FromArgb(Convert.ToInt32("0x36CE2968", 16)),
                Color.FromArgb(Convert.ToInt32("0x93F7417A", 16)),
                Color.FromArgb(Convert.ToInt32("0xA0A52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x939C236B", 16)),
                Color.FromArgb(Convert.ToInt32("0xA08D175B", 16)),
                Color.FromArgb(Convert.ToInt32("0xFDCE2968", 16)),
                Color.FromArgb(Convert.ToInt32("0xB5F7417A", 16)),
                Color.FromArgb(Convert.ToInt32("0x6F00E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x59A52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x5F9C236B", 16)),
                Color.FromArgb(Convert.ToInt32("0x3A8D175B", 16)),
                Color.FromArgb(Convert.ToInt32("0x2ACE2968", 16)),
                Color.FromArgb(Convert.ToInt32("0x68F7417A", 16)),

                Color.FromArgb(Convert.ToInt32("0x495D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0x21641FA1", 16)),
                Color.FromArgb(Convert.ToInt32("0xD17033BC", 16)),
                Color.FromArgb(Convert.ToInt32("0x63621D9D", 16)),
                Color.FromArgb(Convert.ToInt32("0xB57332BC", 16)),
                Color.FromArgb(Convert.ToInt32("0x6F6D2FBE", 16)),
                Color.FromArgb(Convert.ToInt32("0x59F5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0x5F5D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0x3A641FA1", 16)),
                Color.FromArgb(Convert.ToInt32("0x2A7033BC", 16)),
                Color.FromArgb(Convert.ToInt32("0x68621D9D", 16)),
                Color.FromArgb(Convert.ToInt32("0x497332BC", 16)),
                Color.FromArgb(Convert.ToInt32("0x216D2FBE", 16)),
                Color.FromArgb(Convert.ToInt32("0xD15D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0x63641FA1", 16)),
                Color.FromArgb(Convert.ToInt32("0x477033BC", 16)),
                Color.FromArgb(Convert.ToInt32("0x4D621D9D", 16)),
                Color.FromArgb(Convert.ToInt32("0xE97332BC", 16)),
                Color.FromArgb(Convert.ToInt32("0xA56D2FBE", 16)),
                Color.FromArgb(Convert.ToInt32("0x94F5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0xC15D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0x6D641FA1", 16)),
                Color.FromArgb(Convert.ToInt32("0x8F7033BC", 16)),
                Color.FromArgb(Convert.ToInt32("0x4F621D9D", 16)),
                Color.FromArgb(Convert.ToInt32("0xDE7332BC", 16)),
                Color.FromArgb(Convert.ToInt32("0x216D2FBE", 16)),

                Color.FromArgb(Convert.ToInt32("0x50FA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xB1C16D75", 16)),
                Color.FromArgb(Convert.ToInt32("0xDEDF7C87", 16)),
                Color.FromArgb(Convert.ToInt32("0xD9E98F99", 16)),
                Color.FromArgb(Convert.ToInt32("0xC1E87580", 16)),
                Color.FromArgb(Convert.ToInt32("0x6DE9537F", 16)),

                Color.FromArgb(Convert.ToInt32("0x8FFF4DE9", 16)), // Pink

                Color.FromArgb(Convert.ToInt32("0x8FFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x4FC16D75", 16)),
                Color.FromArgb(Convert.ToInt32("0xDEDF7C87", 16)),
                Color.FromArgb(Convert.ToInt32("0x21E98F99", 16)),
                Color.FromArgb(Convert.ToInt32("0x50E87580", 16)),
                Color.FromArgb(Convert.ToInt32("0xB1E9537F", 16)),

                Color.FromArgb(Convert.ToInt32("0xDEFF4DE9", 16)), // Pink

                Color.FromArgb(Convert.ToInt32("0xDEFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xD9C16D75", 16)),
                Color.FromArgb(Convert.ToInt32("0xAEDF7C87", 16)),
                Color.FromArgb(Convert.ToInt32("0x71E98F99", 16)),
                Color.FromArgb(Convert.ToInt32("0xD4E87580", 16)),
                Color.FromArgb(Convert.ToInt32("0x81E9537F", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFDF7C87", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE98F99", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE87580", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE9537F", 16)),

                Color.FromArgb(Convert.ToInt32("0x7528C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xDA1BC8E2", 16)),
                Color.FromArgb(Convert.ToInt32("0x2702C3E4", 16)),
                Color.FromArgb(Convert.ToInt32("0x8C05CBE5", 16)),
                Color.FromArgb(Convert.ToInt32("0xCD50FDFC", 16)),
                Color.FromArgb(Convert.ToInt32("0x7D02BCDF", 16)),

                Color.FromArgb(Convert.ToInt32("0x64FF4DE9", 16)), // Pink
                Color.FromArgb(Convert.ToInt32("0x7DF5EC00", 16)), // Yellow

                Color.FromArgb(Convert.ToInt32("0x6428C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x821BC8E2", 16)),
                Color.FromArgb(Convert.ToInt32("0x2B02C3E4", 16)),
                Color.FromArgb(Convert.ToInt32("0xF005CBE5", 16)),
                Color.FromArgb(Convert.ToInt32("0x3050FDFC", 16)),
                Color.FromArgb(Convert.ToInt32("0x3A02BCDF", 16)),

                Color.FromArgb(Convert.ToInt32("0x2DFF4DE9", 16)), // Pink
                Color.FromArgb(Convert.ToInt32("0x8CF5EC00", 16)), // Yellow

                Color.FromArgb(Convert.ToInt32("0x8328C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x591BC8E2", 16)),
                Color.FromArgb(Convert.ToInt32("0xB302C3E4", 16)),
                Color.FromArgb(Convert.ToInt32("0x5505CBE5", 16)),
                Color.FromArgb(Convert.ToInt32("0x5450FDFC", 16)),
                Color.FromArgb(Convert.ToInt32("0x8402BCDF", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFE91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xFFEF32BF", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xFF28C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFF1BC8E2", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFF910E5A", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xFFFF4DE9", 16)), // Pink
                Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0xFFA52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFF9C236B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFF8D175B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFFCE2968", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFF5D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFF641FA1", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0xFF7033BC", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFFA52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFF9C236B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFF8D175B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFFCE2968", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0xFFF7417A", 16)), // Red-Purple

                Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue

                Color.FromArgb(Convert.ToInt32("0x64E91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0x82EF32BF", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0x2B28C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xF0FA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x301BC8E2", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x3AC16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x2D910E5A", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0x8CFF4DE9", 16)), // Pink
                Color.FromArgb(Convert.ToInt32("0x83F5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0x59A52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xB39C236B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x55FA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x548D175B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x84C16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x7FCE2968", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x0A5D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0x1100E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xC6641FA1", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0x477033BC", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0x1800E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x30A52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xA9FA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x4DC16D75", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xB59C236B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x0C8D175B", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x0800E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x33CE2968", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0x41F5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0x6EF7417A", 16)), // Red-Purple
            };

            var reds = new Color[]
            {
                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),
            };

            var greens = new Color[]
            {
                Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xc7, Color.ForestGreen),

                Color.FromArgb(0xD9, Color.LawnGreen),
                Color.FromArgb(0xEF, Color.PaleGreen),

                Color.FromArgb(0xB6, Color.SeaGreen),

                Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xc7, Color.ForestGreen),

                Color.FromArgb(0xD9, Color.LawnGreen),
                Color.FromArgb(0xEF, Color.PaleGreen),

                Color.FromArgb(0xB6, Color.SeaGreen),

                Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xc7, Color.ForestGreen),

                Color.FromArgb(0xD9, Color.LawnGreen),
                Color.FromArgb(0xEF, Color.PaleGreen),

                Color.FromArgb(0xB6, Color.SeaGreen),
            };

            var redGreenWhite = new Color[]
            {
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(0xFF, Color.White),
            };

            var redGreenWhiteAsymmetric = new Color[]
            {
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),

                Color.FromArgb(0xFF, Color.White),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xFF, Color.White),
            };

            var redGreenWhiteLong = new Color[]
            {
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),

                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
                Color.FromArgb(Convert.ToInt32("0xce1127", 16)),

                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),

                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(0xc7, Color.ForestGreen),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),
                Color.FromArgb(Convert.ToInt32("0x006847", 16)),

                Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xc7, Color.ForestGreen),

                Color.FromArgb(0xD9, Color.LawnGreen),
                Color.FromArgb(0xEF, Color.PaleGreen),

                Color.FromArgb(0xB6, Color.SeaGreen),

                Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xc7, Color.ForestGreen),

                Color.FromArgb(0xD9, Color.LawnGreen),
                Color.FromArgb(0xEF, Color.PaleGreen),

                Color.FromArgb(0xB6, Color.SeaGreen),

                Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xc7, Color.ForestGreen),

                Color.FromArgb(0xD9, Color.LawnGreen),
                Color.FromArgb(0xEF, Color.PaleGreen),

                Color.FromArgb(0xB6, Color.SeaGreen),

                Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xc7, Color.ForestGreen),

                Color.FromArgb(0xD9, Color.LawnGreen),
                Color.FromArgb(0xEF, Color.PaleGreen),

                Color.FromArgb(0xB6, Color.SeaGreen),

                Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xc7, Color.ForestGreen),

                Color.FromArgb(0xD9, Color.LawnGreen),
                Color.FromArgb(0xEF, Color.PaleGreen),

                Color.FromArgb(0xB6, Color.SeaGreen),

                Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xc7, Color.ForestGreen),

                Color.FromArgb(0xD9, Color.LawnGreen),
                Color.FromArgb(0xEF, Color.PaleGreen),

                Color.FromArgb(0xB6, Color.SeaGreen),

                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

                Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xc7, Color.DarkRed),

                Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),
            };

            var bestButt = new Color[]
            {
                Color.FromArgb(Convert.ToInt32("0x52FFFF00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0x410000FF", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x6EFFFF00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0x520000FF", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x90FFFF00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0xF40000FF", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xC9FFFF00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0x870000FF", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x4D0000FF", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x920000FF", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x730000FF", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x2D0000FF", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xA0FFFF00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0x7BFFFF00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0x49FFFF00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0xB3FFFF00", 16)), // Yellow
            };

            var dragonCyborg1 = new Color[] //1
            {
                Color.FromArgb(Convert.ToInt32("0xF0FE6AFF", 16)), // Purpulish
                Color.FromArgb(Convert.ToInt32("0xC473D7E7", 16)), // Blueish
                Color.FromArgb(Convert.ToInt32("0xEB8D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0xA0FF00FF", 16)), // Purple AF
                Color.FromArgb(Convert.ToInt32("0xDD4BE8FA", 16)), // Blueish 2
                Color.FromArgb(Convert.ToInt32("0xF2541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0xE73287A1", 16)), // Turquoisish

                Color.FromArgb(Convert.ToInt32("0x968D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0x4173D7E7", 16)), // Blueish
                Color.FromArgb(Convert.ToInt32("0xB33287A1", 16)), // Turquoisish
                Color.FromArgb(Convert.ToInt32("0x8A73D7E7", 16)), // Blueish
                Color.FromArgb(Convert.ToInt32("0xB08D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0xD6FF00FF", 16)), // Purple AF
                Color.FromArgb(Convert.ToInt32("0xEB8D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0x943287A1", 16)), // Turquoisish
                Color.FromArgb(Convert.ToInt32("0xF073D7E7", 16)), // Blueish
                Color.FromArgb(Convert.ToInt32("0xC43287A1", 16)), // Turquoisish
                Color.FromArgb(Convert.ToInt32("0xEB8D3FD1", 16)), // Purpulish 2

                Color.FromArgb(Convert.ToInt32("0xA0541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0xDD4BE8FA", 16)), // Blueish 2
                Color.FromArgb(Convert.ToInt32("0xF2541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0xE74BE8FA", 16)), // Blueish 2
                Color.FromArgb(Convert.ToInt32("0x96541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0x41FF00FF", 16)), // Purple AF
                Color.FromArgb(Convert.ToInt32("0xB38D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0x8A541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0xB08D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0xD6FF00FF", 16)), // Purple AF
                Color.FromArgb(Convert.ToInt32("0xEB4BE8FA", 16)), // Blueish 2
                Color.FromArgb(Convert.ToInt32("0x94541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0xDB4BE8FA", 16)), // Blueish 2

                Color.FromArgb(Convert.ToInt32("0x068D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0x46541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0x288D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0x504BE8FA", 16)), // Blueish 2
                Color.FromArgb(Convert.ToInt32("0xE9541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0xDE8D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0x44541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0x468D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0x544BE8FA", 16)), // Blueish 2
                Color.FromArgb(Convert.ToInt32("0xA9541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0x8B8D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0x98541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0x558D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0xC0541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0x548D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0xA94BE8FA", 16)), // Blueish 2
                Color.FromArgb(Convert.ToInt32("0x8B541141", 16)), // Dark Purpleish
                Color.FromArgb(Convert.ToInt32("0x988D3FD1", 16)), // Purpulish 2
                Color.FromArgb(Convert.ToInt32("0x554BE8FA", 16)), // Blueish 2
                Color.FromArgb(Convert.ToInt32("0xC0541141", 16)), // Dark Purpleish
            };

            var dragonCyborg2 = new Color[] //2
            {
                Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish


                Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x9353CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish

                Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0xACFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xCFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xF1FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x28FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
            };

            var dragonCyborg2Lucidas = new Color[] //2
            {
                Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

                Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

                Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

                Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

                Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x9353CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x9353CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x9353CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x9353CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

                Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

                Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

                Color.FromArgb(Convert.ToInt32("0xACFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xCFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xF1FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x28FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
            };

            var dragonCyborg2Asymmetric = new Color[] //2
            {
                Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange


                Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange

                Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

                Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange

                Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

                Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green

                Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

                Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green

                Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish

                Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish

                Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green

                Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
                Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

                Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

                Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

                Color.FromArgb(Convert.ToInt32("0xACFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xCFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xF1FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x28FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
            };

            var dragonCyborg2LucidasLong = new Color[] //2
            {
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
                Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange

                Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
                Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green
                Color.FromArgb(Convert.ToInt32("0xACFF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0xCFFF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0xF1FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x28FF8533", 16)), // Bright orange
                Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
                Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
            };

            var dragonCyborg3 = new Color[] //3
            {
                Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0x54BB264C", 16)), // Pink-red

                Color.FromArgb(Convert.ToInt32("0xC5E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0x1AD62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xC5BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xAAEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0xD2BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xBBFFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0xC9E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0x0ED62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xECBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x42EB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0x6EEA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0xEFBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x32FFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0xBBBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x83FFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0x6ABB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x8EE05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x13A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0xB9FFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0x1FBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x12E05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0xF7A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x87A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x68FFA1D5", 16)), // Peach-pink

            };

            var dragonCyborg3Lucidas = new Color[] //3
            {
                Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0x54BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xC5E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0x1AD62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xC5BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xAAEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0xD2BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xBBFFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0x54BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xC5E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0x1AD62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xC5BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xAAEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0xD2BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xBBFFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0x54BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xC5E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0x1AD62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xC5BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xAAEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0xD2BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xBBFFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0x54BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xC5E57FEF", 16)), // Pinkish
                Color.FromArgb(Convert.ToInt32("0x1AD62FDE", 16)), // Pinkish 2
                Color.FromArgb(Convert.ToInt32("0xC5BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xAAEB0DEC", 16)), // Pinkish 3
                Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0xD2BB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xBBFFA1D5", 16)), // Peach-pink

                Color.FromArgb(Convert.ToInt32("0xBBBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x83FFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0x6ABB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x8EE05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x13A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0xB9FFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0x1FBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x12E05D46", 16)), // Orangeish

                Color.FromArgb(Convert.ToInt32("0xBBBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x83FFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0x6ABB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x8EE05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x13A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0xB9FFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0x1FBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x12E05D46", 16)), // Orangeish

                Color.FromArgb(Convert.ToInt32("0xBBBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x83FFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0x6ABB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x8EE05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x13A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0xB9FFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0x1FBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x12E05D46", 16)), // Orangeish

                Color.FromArgb(Convert.ToInt32("0xBBBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x83FFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0x6ABB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x8EE05D46", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x13A63056", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0xB9FFA1D5", 16)), // Peach-pink
                Color.FromArgb(Convert.ToInt32("0x1FBB264C", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x12E05D46", 16)), // Orangeish
            };

            var nochesCincoDeMayo1 = new Color[] //3
            {
                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
                Color.FromArgb(Convert.ToInt32("0xf98f21", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x25bcc0", 16)), // Blueish
                Color.FromArgb(Convert.ToInt32("0x234c91", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
            };

            var nochesCincoDeMayo1Asymmetric = new Color[] //3
            {
                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
                Color.FromArgb(Convert.ToInt32("0x25bcc0", 16)), // Blueish
                Color.FromArgb(Convert.ToInt32("0x234c91", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish

                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
                Color.FromArgb(Convert.ToInt32("0x25bcc0", 16)), // Blueish
                Color.FromArgb(Convert.ToInt32("0x234c91", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish

                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
                Color.FromArgb(Convert.ToInt32("0x25bcc0", 16)), // Blueish
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish

                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
                Color.FromArgb(Convert.ToInt32("0xf98f21", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x234c91", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
            };

            var nochesCincoDeMayo1Long = new Color[] //3
            {
                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xf98f21", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0x21F5E13B", 16)), // Orange-yellowish
                Color.FromArgb(Convert.ToInt32("0x8FED6259", 16)), // Redish
                Color.FromArgb(Convert.ToInt32("0x7AE83D46", 16)), // Redish 2
                Color.FromArgb(Convert.ToInt32("0x9DFFA44F", 16)), // Redish-orange
                Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
                Color.FromArgb(Convert.ToInt32("0x83AC77BD", 16)), // Washed purple
                Color.FromArgb(Convert.ToInt32("0x6AEF2416", 16)), // Lenseflare redish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
                Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
                Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
                Color.FromArgb(Convert.ToInt32("0xFFF8B532", 16)), // Golden orange
            };

            var dragonCyborg5 = new Color[] //5
            {
                Color.FromArgb(Convert.ToInt32("0xAEFF50E9", 16)), // Bright pinkish
                Color.FromArgb(Convert.ToInt32("0x22B634D4", 16)), // Dark purpleish
                Color.FromArgb(Convert.ToInt32("0x9E6300AC", 16)), // Dark purpleish 2
                Color.FromArgb(Convert.ToInt32("0x6ED10163", 16)), // Deep pinkish
                Color.FromArgb(Convert.ToInt32("0xAFFF295A", 16)), // Pink orangeish
                Color.FromArgb(Convert.ToInt32("0x96FF4A6B", 16)), // Lighter redish
                Color.FromArgb(Convert.ToInt32("0xDEE90041", 16)), // Deeper redish
                Color.FromArgb(Convert.ToInt32("0x0332179C", 16)), // Blueish purple
                Color.FromArgb(Convert.ToInt32("0x09532A88", 16)), // Dark purpleish
                Color.FromArgb(Convert.ToInt32("0x127D0ED9", 16)), // Ethereal purple
            };

            var dragonCyborg6 = new Color[] //6
            {
                Color.FromArgb(Convert.ToInt32("0xFFFD8342", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0xFFEF2416", 16)), // Lenseflare redish
                Color.FromArgb(Convert.ToInt32("0xFF26C5E6", 16)), // Forest green-blue
                Color.FromArgb(Convert.ToInt32("0xFFF8B532", 16)), // Golden orange
                Color.FromArgb(Convert.ToInt32("0xFF3E9F5A", 16)), // Greenish
                Color.FromArgb(Convert.ToInt32("0xFF0F90D2", 16)), // Blueish
                Color.FromArgb(Convert.ToInt32("0xFFD840A5", 16)), // Deep pinkish
                Color.FromArgb(Convert.ToInt32("0xFFD44E89", 16)), // Different pinkish
                Color.FromArgb(Convert.ToInt32("0xFFF57457", 16)), // Peach orangeish
                Color.FromArgb(Convert.ToInt32("0xFFAC77BD", 16)), // Washed purple

                Color.FromArgb(Convert.ToInt32("0x7EEF2416", 16)), // Lenseflare redish
                Color.FromArgb(Convert.ToInt32("0x39F8B532", 16)), // Golden orange
                Color.FromArgb(Convert.ToInt32("0x3ED840A5", 16)), // Deep pinkish
                Color.FromArgb(Convert.ToInt32("0xC1AC77BD", 16)), // Washed purple
                Color.FromArgb(Convert.ToInt32("0x7FF57457", 16)), // Peach orangeish
                Color.FromArgb(Convert.ToInt32("0x0CEF2416", 16)), // Lenseflare redish
                Color.FromArgb(Convert.ToInt32("0x3DF8B532", 16)), // Golden orange
                Color.FromArgb(Convert.ToInt32("0x6DAC77BD", 16)), // Washed purple
                Color.FromArgb(Convert.ToInt32("0xC0D840A5", 16)), // Deep pinkish
                Color.FromArgb(Convert.ToInt32("0xB9F57457", 16)), // Peach orangeish

                Color.FromArgb(Convert.ToInt32("0xC2D840A5", 16)), // Deep pinkish
                Color.FromArgb(Convert.ToInt32("0xC5AC77BD", 16)), // Washed purple
                Color.FromArgb(Convert.ToInt32("0x81EF2416", 16)), // Lenseflare redish
                Color.FromArgb(Convert.ToInt32("0x0426C5E6", 16)), // Forest green-blue
                Color.FromArgb(Convert.ToInt32("0xA2D840A5", 16)), // Deep pinkish
                Color.FromArgb(Convert.ToInt32("0x5AAC77BD", 16)), // Washed purple
                Color.FromArgb(Convert.ToInt32("0xC5EF2416", 16)), // Lenseflare redish
                Color.FromArgb(Convert.ToInt32("0xACD840A5", 16)), // Deep pinkish
                Color.FromArgb(Convert.ToInt32("0x6226C5E6", 16)), // Forest green-blue
                Color.FromArgb(Convert.ToInt32("0x83AC77BD", 16)), // Washed purple
                Color.FromArgb(Convert.ToInt32("0x6AEF2416", 16)), // Lenseflare redish
            };

            var dragonCyborg8 = new Color[] //9
            {
                Color.FromArgb(Convert.ToInt32("0x46ED6259", 16)), // Redish
                Color.FromArgb(Convert.ToInt32("0x2CE83D46", 16)), // Redish 2
                Color.FromArgb(Convert.ToInt32("0x7AFFA44F", 16)), // Redish-orange
                Color.FromArgb(Convert.ToInt32("0x21F5E13B", 16)), // Orange-yellowish

                Color.FromArgb(Convert.ToInt32("0x6C4CEDE0", 16)), // turquoiseish
                Color.FromArgb(Convert.ToInt32("0x3B3887B8", 16)), // Dark turquoiseish
                Color.FromArgb(Convert.ToInt32("0x1D3787D6", 16)), // Dark blueish
                Color.FromArgb(Convert.ToInt32("0x2D9E2CDB", 16)), // Purpleish
                Color.FromArgb(Convert.ToInt32("0xA4BC2BD4", 16)), // Deep pinkish
                Color.FromArgb(Convert.ToInt32("0x1CD427B8", 16)), // Pink

                Color.FromArgb(Convert.ToInt32("0xBFD41C64", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x8FED6259", 16)), // Redish
                Color.FromArgb(Convert.ToInt32("0x7AE83D46", 16)), // Redish 2
                Color.FromArgb(Convert.ToInt32("0xE83887B8", 16)), // Dark turquoiseish
                Color.FromArgb(Convert.ToInt32("0x9DFFA44F", 16)), // Redish-orange
                Color.FromArgb(Convert.ToInt32("0xD6F5E13B", 16)), // Orange-yellowish
                Color.FromArgb(Convert.ToInt32("0x69D41C64", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x79ED6259", 16)), // Redish
                Color.FromArgb(Convert.ToInt32("0x503787D6", 16)), // Dark blueish
                Color.FromArgb(Convert.ToInt32("0xEB9E2CDB", 16)), // Purpleish
                Color.FromArgb(Convert.ToInt32("0x124CEDE0", 16)), // turquoiseish
                Color.FromArgb(Convert.ToInt32("0x48E83D46", 16)), // Redish 2
                Color.FromArgb(Convert.ToInt32("0xC9FFA44F", 16)), // Redish-orange
                Color.FromArgb(Convert.ToInt32("0xDFF5E13B", 16)), // Orange-yellowish
            };

            var dragonCyborg9 = new Color[] //9
            {
                Color.FromArgb(Convert.ToInt32("0x1FE00206", 16)), // Redish
                Color.FromArgb(Convert.ToInt32("0x31F97200", 16)), // Red-orange
                Color.FromArgb(Convert.ToInt32("0xA8FCC209", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x26F2DD41", 16)), // Yellowish

                Color.FromArgb(Convert.ToInt32("0xD458D969", 16)), // Green
                Color.FromArgb(Convert.ToInt32("0x4B03D901", 16)), // Brighter green
                Color.FromArgb(Convert.ToInt32("0x9E07C4D9", 16)), // Background blue
                Color.FromArgb(Convert.ToInt32("0x950060BD", 16)), // Darker background blue
                Color.FromArgb(Convert.ToInt32("0x8B9178CF", 16)), // Faded purple
                Color.FromArgb(Convert.ToInt32("0x72A951D6", 16)), // Richer purple
                Color.FromArgb(Convert.ToInt32("0x26EC87FF", 16)), // Purple-pink

                Color.FromArgb(Convert.ToInt32("0x26D41C64", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xECE41BC3", 16)), // Red-pink

                Color.FromArgb(Convert.ToInt32("0x08E00206", 16)), // Redish
                Color.FromArgb(Convert.ToInt32("0x98F97200", 16)), // Red-orange
                Color.FromArgb(Convert.ToInt32("0x16FCC209", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x20E00206", 16)), // Redish
                Color.FromArgb(Convert.ToInt32("0x42F97200", 16)), // Red-orange
                Color.FromArgb(Convert.ToInt32("0x2DFCC209", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x7AD41C64", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0xA1E41BC3", 16)), // Red-pink
                Color.FromArgb(Convert.ToInt32("0x9CFCC209", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x49F2DD41", 16)), // Yellowish

                Color.FromArgb(Convert.ToInt32("0x090060BD", 16)), // Darker background blue
                Color.FromArgb(Convert.ToInt32("0xCB9178CF", 16)), // Faded purple
                Color.FromArgb(Convert.ToInt32("0x03A951D6", 16)), // Richer purple
                Color.FromArgb(Convert.ToInt32("0xF7F97200", 16)), // Red-orange
                Color.FromArgb(Convert.ToInt32("0x30EC87FF", 16)), // Purple-pink
                Color.FromArgb(Convert.ToInt32("0xAE0060BD", 16)), // Darker background blue
                Color.FromArgb(Convert.ToInt32("0x519178CF", 16)), // Faded purple
                Color.FromArgb(Convert.ToInt32("0x93D41C64", 16)), // Pink-red
                Color.FromArgb(Convert.ToInt32("0x72A951D6", 16)), // Richer purple
                Color.FromArgb(Convert.ToInt32("0xEDEC87FF", 16)), // Purple-pink
            };

            var frequentDragonCyborg9 = new Color[] //9
            {
                Color.FromArgb(Convert.ToInt32("0x1FE00206", 16)), // Redish
                Color.FromArgb(0xFF, Color.Orange), // Orange
                Color.FromArgb(Convert.ToInt32("0xA8FCC209", 16)), // Orangeish

                Color.FromArgb(Convert.ToInt32("0x4B03D901", 16)), // Brighter green
                Color.FromArgb(Convert.ToInt32("0x9E07C4D9", 16)), // Background blue
                Color.FromArgb(Convert.ToInt32("0x8B9178CF", 16)), // Faded purple
                Color.FromArgb(Convert.ToInt32("0x72E823D6", 16)), // Richer purple
                Color.FromArgb(Convert.ToInt32("0x26FF23FF", 16)), // Purple-pink
            };

            var plc = new Color[]
            {
                Color.FromArgb(Convert.ToInt32("0xA8FCC209", 16)), // Orangeish
                Color.FromArgb(Convert.ToInt32("0x72E823D6", 16)), // Richer purple
                Color.FromArgb(Convert.ToInt32("0x26FF23FF", 16)), // Purple-pink
                Color.FromArgb(Convert.ToInt32("0xD458D969", 16)), // Green
                Color.FromArgb(0XFD, Color.Orange),
                Color.FromArgb(0xD8, Color.Purple),
                Color.FromArgb(0xA1, 0x00, 0x00, 0xFF),
            };

            var dragChurch = new Color[]
            {
                // Color.FromArgb(Convert.ToInt32("0x00AEEEEE", 16)), // Baby Pink
                // Color.FromArgb(Convert.ToInt32("0x00F4C2C2", 16)), // Baby Pink
                // Color.FromArgb(Convert.ToInt32("0x00FFF5BA", 16)), // Lavender
                // Color.FromArgb(Convert.ToInt32("0x00C1E1C1", 16)), // Mint Green
                // Color.FromArgb(Convert.ToInt32("0x00CDE7F0", 16)), // Baby Blue
                // Color.FromArgb(Convert.ToInt32("0x00FFF5BA", 16)), // Pale Yellow

                // Color.FromArgb(Convert.ToInt32("0x00FF9EC7", 16)), // Baby Pink
                // Color.FromArgb(Convert.ToInt32("0x00FFE066", 16)), // Baby Pink
                // Color.FromArgb(Convert.ToInt32("0x00C79BFF", 16)), // Lavender
                // Color.FromArgb(Convert.ToInt32("0x007EE4B8", 16)), // Mint Green
                // Color.FromArgb(Convert.ToInt32("0x00CDE7F0", 16)), // Baby Blue
                // Color.FromArgb(Convert.ToInt32("0x006FE7E7", 16)), // Pale Yellow

                Color.FromArgb(Convert.ToInt32("0x00FF7FB8", 16)), // Pink (more vivid, less white)
                Color.FromArgb(Convert.ToInt32("0x00FFD633", 16)), // Yellow (richer but still safe)
                Color.FromArgb(Convert.ToInt32("0x00B574FF", 16)), // Lavender (deeper purple)
                Color.FromArgb(Convert.ToInt32("0x005FE0A6", 16)), // Mint (more color, less milky)
                Color.FromArgb(Convert.ToInt32("0x009EDAF0", 16)), // Baby Blue (cleaner cyan-blue)
                Color.FromArgb(Convert.ToInt32("0x004FDCDC", 16)), // Robin Egg (more saturated cyan)

                Color.FromArgb(Convert.ToInt32("0x00FF66A8", 16)), // Pink (punchy but still soft)
                Color.FromArgb(Convert.ToInt32("0x00FFCC1A", 16)), // Yellow (approaching gold, still clean)
                Color.FromArgb(Convert.ToInt32("0x009F5BFF", 16)), // Lavender (vivid lilac)
                Color.FromArgb(Convert.ToInt32("0x004DD89C", 16)), // Mint (stronger green tint)
                Color.FromArgb(Convert.ToInt32("0x007FD0F0", 16)), // Baby Blue (more defined blue)
                Color.FromArgb(Convert.ToInt32("0x003AD6D6", 16)), // Robin Egg (crisper cyan)
            };

            _keyAnimations = new IKeyAnimation[]
            {
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, plc, _audioService, _cts.Token, fastAnimate: true),
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, nochesCincoDeMayo1Long, _audioService, _cts.Token, fastAnimate: true),
                new RainbowAnimation(_pixelChannels.TotalPixelCount, dragChurch, _audioService, _cts.Token, fastAnimate: true),
                new RainbowAnimation(_pixelChannels.TotalPixelCount, warmLucidas, _audioService, _cts.Token, fastAnimate: true),
                new RainbowAnimation(_pixelChannels.TotalPixelCount, dragonCyborg9, _audioService, _cts.Token, fastAnimate: true),
                new RainbowAnimation(_pixelChannels.TotalPixelCount, dragonCyborg2Lucidas, _audioService, _cts.Token, fastAnimate: true),
                new VibrantAnimation(_pixelChannels.TotalPixelCount, _audioService, _graphicsHost, Shaders.Geometry, new Color[]
                {
                    Color.Red,
                    Color.FromArgb(255, 0, 137),
                    Color.FromArgb(255, 0, 35),
                    Color.FromArgb(67, 112, 0),
                    Color.FromArgb(255, 74, 0),
                    Color.FromArgb(9, 254, 182)
                }, _cts.Token),
                new VibrantAnimation(_pixelChannels.TotalPixelCount, _audioService, _graphicsHost, Shaders.Kaleidoscope, new Color[]
                {
                    Color.Red,
                    Color.FromArgb(255, 0, 137),
                    Color.FromArgb(255, 0, 35),
                    Color.FromArgb(67, 112, 0),
                    Color.FromArgb(255, 74, 0),
                    Color.FromArgb(9, 254, 182)
                }, _cts.Token),
                new VibrantAnimation(_pixelChannels.TotalPixelCount, _audioService, _graphicsHost, Shaders.Clown, new Color[]
                {
                    Color.FromArgb(220,  20,  20), // red
                    Color.FromArgb(255, 200,   0), // yellow
                    Color.FromArgb(  0, 200, 220), // cyan
                    Color.FromArgb(230,   0, 180), // hot pink
                    Color.FromArgb(255, 100,   0), // orange
                    Color.FromArgb(  0, 200,  30), // lime green
                    Color.FromArgb(150,   0, 220), // purple
                    Color.FromArgb(  0,  60, 230), // electric blue
                    Color.FromArgb(220,  50,  50), // tomato
                    Color.FromArgb(160, 220,   0), // chartreuse
                    Color.FromArgb(  0, 180, 160), // teal
                    Color.FromArgb(220,   0, 100), // deep rose
                    Color.FromArgb(255, 140,   0), // amber
                    Color.FromArgb(120,   0, 200), // violet
                    Color.FromArgb(  0, 220, 120), // seafoam
                    Color.FromArgb(200,   0,  40), // crimson
                }, _cts.Token)
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, dragonCyborg8, _audioService, _cts.Token, fastAnimate: true),
            };
            _auxilaryKeyAnimations = new IKeyAnimation[]
            {
                //new RainbowAnimation(_parChannels.TotalPixelCount, plc, _audioService, _cts.Token),
                //new RainbowAnimation(_parChannels.TotalPixelCount, nochesCincoDeMayo1Long, _audioService, _cts.Token),
                new RainbowAnimation(_parChannels.TotalPixelCount, dragChurch, _audioService, _cts.Token),
                new RainbowAnimation(_parChannels.TotalPixelCount, warmLucidas, _audioService, _cts.Token),
                new RainbowAnimation(_parChannels.TotalPixelCount, dragonCyborg9, _audioService, _cts.Token),
                new RainbowAnimation(_parChannels.TotalPixelCount, dragonCyborg2Lucidas, _audioService, _cts.Token),
                new RainbowAnimation(_parChannels.TotalPixelCount, dragonCyborg6, _audioService, _cts.Token),
                new RainbowAnimation(_parChannels.TotalPixelCount, dragonCyborg6, _audioService, _cts.Token),
                new RainbowAnimation(_parChannels.TotalPixelCount, dragonCyborg6, _audioService, _cts.Token),
                //commented so we don't have multiple calls to graphics host
                //new VibrantAnimation(_parChannels.TotalPixelCount, _audioService, _graphicsHost, _cts.Token)
                //new RainbowAnimation(_parChannels.TotalPixelCount, dragonCyborg8, _audioService, _cts.Token),
            };

            _transformAnimations = new ITransformAnimation[]
            {
                //new MirrorTransformAnimation(),
                //new WaveDim(),
                //new FlyingSaucer(_audioService, _cts.Token),
            };

            InitializePads();

            SetNavigationLevel(NavigationLevel.Root);

            for (int i = 0; i < _keyAnimations.Length; i++)
            {
                var animation = _keyAnimations[i];
                if (animation is MidiAwareAnimation midiAware)
                {
                    midiAware.MidiAware_DeviceStateChangeEvent += MidiAware_OnDeviceStateChange;
                }
            }
        }

        private void InitializePads()
        {
            _padStates = new PadState[_inputDevice.PadCount];
            for (int i = 0; i < _keyAnimations.Length && i < _padTransitionStartIndex; i++)
            {
                _padStates[i].Color = System.Drawing.Color.Red;
                _padStates[i].PadIllumination = PadIllumination.Solid;
            }

            for (int i = 0; i < _transformAnimations.Length && i < _padTransitionStartIndex; i++)
            {
                _padStates[i + _padTransitionStartIndex].Color = System.Drawing.Color.Red;
                _padStates[i + _padTransitionStartIndex].PadIllumination = PadIllumination.Solid;
            }
        }

        private void RenderThreadLoop()
        {
            byte[] targetVector = new byte[22460];
            try
            {
            // Pre-warm MatchIP routing caches by sending sequentially before the main loop.
            // ArtNetSharp.NetworkClientBag.MatchIP caches routing results in non-thread-safe
            // List<IPAddress> fields. If the first frame fires all sends in parallel and
            // GetLocalIP fails during a race, the destination IP gets permanently blacklisted
            // for the lifetime of the NetworkClientBag. Sequential sends here populate the
            // cache once without contention.
            foreach (IDmxOutputDevice outputDevice in _outputDevices)
                outputDevice.Write(new byte[outputDevice.MaxChannels]).GetAwaiter().GetResult();

            ControllerInstance controllerInstance = new ControllerInstance(ArtNet.Instance);
            try
            {
            while (!_cts.Token.IsCancellationRequested)
            {
                _frameRateStopWatch.Start();
                var frameStart = _frameRateStopWatch.Elapsed;
                IKeyAnimation keyAnimation = null;
                ITransformAnimation transformAnimation = null;
                IKeyAnimation auxilaryAnimation = null;
                lock (_navigationLock)
                {
                    keyAnimation = _activeKeyAnimation > -1 ? _keyAnimations[_activeKeyAnimation] : null;
                    auxilaryAnimation = _activeKeyAnimation > -1 ? _auxilaryKeyAnimations[_activeKeyAnimation] : null;
                    transformAnimation = _activeTransformAnimation > -1 ? _transformAnimations[_activeTransformAnimation] : null;
                }
                int pixelOffset = 0;
                int channelOffset = 0;
                if (keyAnimation != null)
                {
                    var renderSettings = new RenderSettings();
                    renderSettings.MasterFader = _masterFader.InterlockedGet();
                    var frame = keyAnimation.GetNextFrame();
                    if (transformAnimation != null)
                    {
                        frame = transformAnimation.GetNextFrame(frame);
                    }
                    var frameSpan = frame.AsSpan();
                    var surface = _pixelChannels;
                            
                    surface.Render(frameSpan.Slice(pixelOffset, surface.TotalPixelCount), targetVector.AsSpan().Slice(channelOffset, surface.TotalChannelCount), renderSettings);
                    pixelOffset += surface.TotalPixelCount;
                    channelOffset += surface.TotalChannelCount;
                }
                //if (auxilaryAnimation != null)
                //{
                //    var renderSettings = new RenderSettings();
                //    renderSettings.MasterFader = _auxilaryMasterFader.InterlockedGet();
                //    var frame = auxilaryAnimation.GetNextFrame();
                //    var frameSpan = frame.AsSpan();
                //    var surface = _parChannels;

                //    surface.Render(frameSpan, targetVector.AsSpan().Slice(channelOffset, surface.TotalChannelCount), renderSettings);
                //}

                int take = 512;
                int skip = 0;
                var writeTasks = new System.Collections.Generic.List<Task>(_outputDevices.Length);
                foreach (IDmxOutputDevice outputDevice in _outputDevices)
                {
                    //var t = Math.Min(take, outputDevice.MaxChannels);
                    var t = outputDevice.MaxChannels;
                    var output = targetVector.Skip(skip).Take(t).ToArray();
                    writeTasks.Add(outputDevice.Write(output));
                    skip += t;
                }
                Task.WhenAll(writeTasks).GetAwaiter().GetResult();
                _artNetController.SendSync().GetAwaiter().GetResult();

                Thread.Yield();
                var frameEnd = _frameRateStopWatch.Elapsed;
                var sendTime = TimeSpan.FromMilliseconds(25) - (frameEnd - frameStart);
                if (sendTime.TotalMilliseconds > 0)
                {
                    SpinWait.SpinUntil(() => false, sendTime);
                }
            }
            } // try
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in render thread.");
                _tcs.TrySetException(ex);
                return;
            }
            _tcs.SetResult(true);
        }

        private void MidiAware_OnDeviceStateChange(object sender, DeviceStateChangeEventArgs e)
        {
            IKeyAnimation activeKeyAnimation = null;
            NavigationLevel navigationLevel;
            int sourceAnimationIndex = -1;
            lock (_navigationLock)
            {
                if (_activeKeyAnimation > -1)
                {
                    activeKeyAnimation = _keyAnimations[_activeKeyAnimation];
                }
                for (int i = 0; i < _keyAnimations.Length; i++)
                {
                    if (sender == _keyAnimations[i])
                    {
                        sourceAnimationIndex = i;
                    }
                }
                navigationLevel = _navigationLevel;
            }
            if (sender == activeKeyAnimation && e.EventType == DeviceStateChangeEventType.PadColor)
            {
                _inputDevice.SetPadState(e.PadNumber, e.PadState);
            }
            else if (e.EventType == DeviceStateChangeEventType.NavigationColor)
            {
                if (sourceAnimationIndex > -1)
                {
                    _padStates[sourceAnimationIndex].Color = e.NavigationColor;
                    if (navigationLevel == NavigationLevel.Root)
                    {
                        _inputDevice.SetPadState(sourceAnimationIndex, _padStates[sourceAnimationIndex]);
                    }
                }
            }
        }
    }
}
