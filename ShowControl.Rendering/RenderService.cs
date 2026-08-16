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
using static ShowControl.Rendering.Palettes;
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

        private int _masterFader = 255;
        private int _auxilaryMasterFader = 251;

        private Timer NavButtonBlinkTimer;
        private Timer RenderOperatatorTimer;
        private readonly TimeSpan RenderDelayTime = TimeSpan.FromMilliseconds(100);
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

        private void NudgeShaderParams(object state)
        {
            lock (_navigationLock)
            {
                if (_activeKeyAnimation > -1 && _keyAnimations[_activeKeyAnimation] is VibrantAnimation vibrant)
                {
                    vibrant.NudgeParamsTowardDefaults();
                }
            }
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
                    Enumerable.Repeat(WS2812B.Instance, 11520).ToArray()
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
            Initialize();
            // Auto-activate animation 0 before starting the render thread
            // to avoid racing SetShader() with the render loop
            SetActiveKeyAnimation(0);
            SetNavigationLevel(NavigationLevel.KeyAnimation);
            _renderThread.Start();
            //NavButtonBlinkTimer = new Timer(Blink, this, 500, 500);
            //RenderOperatatorTimer = new Timer(ChangeAnimation, this, RenderDelayTime, RenderDelayTime);
            RenderOperatatorTimer = new Timer(NudgeShaderParams, this, Timeout.Infinite, Timeout.Infinite);
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
                    // if (direction == RotaryDirection.Up)
                    // {
                    //     _masterFader.InterlockedDecrement(0, byte.MaxValue);
                    // }
                    // else if (direction == RotaryDirection.Down)
                    // {
                    //     _masterFader.InterlockedIncrement(0, byte.MaxValue);
                    // }
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
            RenderOperatatorTimer.Change(TimeSpan.FromSeconds(5), RenderDelayTime);
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
            // Up button navigation toggle disabled — SetShader pipeline rebuild
            // during runtime can cause GPU hangs on MoltenVK.
            // Animation is auto-started at index 0 on startup.
            // if (direction == ButtonDirection.Press && navigationButton == NavigationButton.Up)
            // {
            //     _inputDevice.ClearInterface();
            //     lock (_navigationLock)
            //     {
            //         SetNavigationLevel(_navigationLevel == NavigationLevel.Root ? NavigationLevel.KeyAnimation : NavigationLevel.Root);
            //     }
            // }
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
                //System.Diagnostics.Debug.WriteLine("Address {0} for {1}: {2}", j + 1, _devices[j].GetType().ToString(), address);
                Console.WriteLine("Address {0} for {1}: {2}", j + 1, _devices[j].GetType().ToString(), address);
                address += _devices[j].ChannelCount;
            }


            _keyAnimations = new IKeyAnimation[]
            {
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, plc, _audioService, _cts.Token, fastAnimate: true),
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, nochesCincoDeMayo1Long, _audioService, _cts.Token, fastAnimate: true),
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, dragChurch, _audioService, _cts.Token, fastAnimate: true),
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, warmLucidas, _audioService, _cts.Token, fastAnimate: true),
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, dragonCyborg9, _audioService, _cts.Token, fastAnimate: true),
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, dragonCyborg2Lucidas, _audioService, _cts.Token, fastAnimate: true),
                // new VibrantAnimation(_pixelChannels.TotalPixelCount, _audioService, _graphicsHost, Shaders.Geometry, new Color[]
                // {
                //     Color.Red,
                //     Color.FromArgb(255, 0, 137),
                //     Color.FromArgb(255, 0, 35),
                //     Color.FromArgb(67, 112, 0),
                //     Color.FromArgb(255, 74, 0),
                //     Color.FromArgb(9, 254, 182)
                // }, _cts.Token),
                new VibrantAnimation(_pixelChannels.TotalPixelCount, _audioService, _graphicsHost, Shaders.Kaleidoscope, frequentWarmColdRoygviv, _cts.Token),
                // new VibrantAnimation(_pixelChannels.TotalPixelCount, _audioService, _graphicsHost, Shaders.Clown, new Color[]
                // {
                //     Color.FromArgb(220,  20,  20), // red
                //     Color.FromArgb(255, 200,   0), // yellow
                //     Color.FromArgb(  0, 200, 220), // cyan
                //     Color.FromArgb(230,   0, 180), // hot pink
                //     Color.FromArgb(255, 100,   0), // orange
                //     Color.FromArgb(  0, 200,  30), // lime green
                //     Color.FromArgb(150,   0, 220), // purple
                //     Color.FromArgb(  0,  60, 230), // electric blue
                //     Color.FromArgb(220,  50,  50), // tomato
                //     Color.FromArgb(160, 220,   0), // chartreuse
                //     Color.FromArgb(  0, 180, 160), // teal
                //     Color.FromArgb(220,   0, 100), // deep rose
                //     Color.FromArgb(255, 140,   0), // amber
                //     Color.FromArgb(120,   0, 200), // violet
                //     Color.FromArgb(  0, 220, 120), // seafoam
                //     Color.FromArgb(200,   0,  40), // crimson
                // }, _cts.Token)
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, dragonCyborg8, _audioService, _cts.Token, fastAnimate: true),
            };
            _auxilaryKeyAnimations = new IKeyAnimation[]
            {
                //new RainbowAnimation(_parChannels.TotalPixelCount, plc, _audioService, _cts.Token),
                //new RainbowAnimation(_parChannels.TotalPixelCount, nochesCincoDeMayo1Long, _audioService, _cts.Token),
                //new RainbowAnimation(_parChannels.TotalPixelCount, dragChurch, _audioService, _cts.Token),
                // new RainbowAnimation(_parChannels.TotalPixelCount, warmLucidas, _audioService, _cts.Token),
                new RainbowAnimation(_parChannels.TotalPixelCount, dragonCyborg9, _audioService, _cts.Token),
                //new RainbowAnimation(_parChannels.TotalPixelCount, dragonCyborg2Lucidas, _audioService, _cts.Token),
                // new RainbowAnimation(_parChannels.TotalPixelCount, dragonCyborg6, _audioService, _cts.Token),
                //new RainbowAnimation(_parChannels.TotalPixelCount, dragonCyborg6, _audioService, _cts.Token),
                //new RainbowAnimation(_parChannels.TotalPixelCount, dragonCyborg6, _audioService, _cts.Token),
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

        // Time-of-day master fader schedule. Waypoints are (hour as decimal, fader value).
        // Linearly interpolates between adjacent waypoints. Wraps around midnight.
        private static readonly (double hour, int value)[] _faderSchedule = new[]
        {
            // (20.0,   255),  // 8:00 PM — start dimming
            // (21.0,   255),  // 9:00 PM
            // (21.75,  248),  // 9:45 PM
            // (22.75,  175),  // 10:45 PM — dimmest
            // (27.5,   175),  // 3:30 AM — start undimming
            // (28.333, 248),  // 4:20 AM  (next day, represented as 24+4.333)
            // (29.15,  255),  // 5:09 AM
            // (29.5,   255),  // 5:30 AM — back to full
            (20.0,   255),  // 8:00 PM — start dimming
            (21.0,   255),  // 9:00 PM
            (21.75,  255),  // 9:45 PM
            (22.75,  255),  // 10:45 PM — dimmest
            (27.5,   255),  // 3:30 AM — start undimming
            (28.333, 255),  // 4:20 AM  (next day, represented as 24+4.333)
            (29.15,  255),  // 5:09 AM
            (29.5,   255),  // 5:30 AM — back to full
        };

        private void UpdateMasterFaderFromSchedule()
        {
            var now = DateTime.Now;
            // Use hours past noon so the evening-to-morning span doesn't cross midnight boundary
            double hour = now.Hour + now.Minute / 60.0 + now.Second / 3600.0;
            if (hour < 12.0) hour += 24.0; // e.g. 4 AM → 28.0

            // Before first waypoint or after last — use nearest endpoint
            if (hour <= _faderSchedule[0].hour)
            {
                _masterFader = _faderSchedule[0].value;
                return;
            }
            if (hour >= _faderSchedule[_faderSchedule.Length - 1].hour)
            {
                _masterFader = _faderSchedule[_faderSchedule.Length - 1].value;
                return;
            }

            for (int i = 0; i < _faderSchedule.Length - 1; i++)
            {
                var (h0, v0) = _faderSchedule[i];
                var (h1, v1) = _faderSchedule[i + 1];
                if (hour >= h0 && hour < h1)
                {
                    double t = (hour - h0) / (h1 - h0);
                    _masterFader = (int)Math.Round(v0 + (v1 - v0) * t);
                    return;
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
            byte[] targetVector = new byte[44920];
            try
            {
                // Pre-warm MatchIP routing caches by sending sequentially before the main loop.
                // ArtNetSharp.NetworkClientBag.MatchIP caches routing results in non-thread-safe
                // List<IPAddress> fields. If the first frame fires all sends in parallel and
                // GetLocalIP fails during a race, the destination IP gets permanently blacklisted
                // for the lifetime of the NetworkClientBag. Sequential sends here populate the
                // cache once without contention.
                foreach (IDmxOutputDevice outputDevice in _outputDevices)
                {
                    outputDevice.Write(new byte[outputDevice.MaxChannels]).GetAwaiter().GetResult();
                }
            }
            catch
            { }

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
                UpdateMasterFaderFromSchedule();
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

                int skip = 0;
                var writeTasks = new System.Collections.Generic.List<Task>(_outputDevices.Length);
                try
                {
                    foreach (IDmxOutputDevice outputDevice in _outputDevices)
                    {
                        var t = outputDevice.MaxChannels;
                        var output = targetVector.Skip(skip).Take(t).ToArray();
                        writeTasks.Add(outputDevice.Write(output));
                        skip += t;
                    }
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                    _logger.LogWarning(ex, "SocketException while queuing output device writes.");
                }
                try { Task.WhenAll(writeTasks).GetAwaiter().GetResult(); }
                catch (Exception ex) { _logger.LogWarning(ex, "One or more output device writes failed."); }
                try { _artNetController.SendSync().GetAwaiter().GetResult(); }
                catch (Exception ex) { _logger.LogWarning(ex, "ArtNet sync failed."); }

                var frameEnd = _frameRateStopWatch.Elapsed;
                var sendTime = TimeSpan.FromMicroseconds(21739) - (frameEnd - frameStart);
                if (sendTime.TotalMilliseconds > 0)
                {
#if WINDOWS
                    // Busy-wait on Windows — SpinWait.SpinUntil uses Environment.TickCount
                    // which has ~15.6ms resolution, causing frame time overshoot
                    var targetEnd = frameStart + TimeSpan.FromMilliseconds(25);
                    while (_frameRateStopWatch.Elapsed < targetEnd)
                    {
                        Thread.SpinWait(100);
                    }
#else
                    SpinWait.SpinUntil(() => false, sendTime);
#endif
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
