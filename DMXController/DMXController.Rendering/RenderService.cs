using ShowControl.Animation;
using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
using ShowControl.Devices.Input;
using ShowControl.Devices.Input.Extensions;
using ShowControl.Devices.Output;
using ShowControl.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        private readonly IDmxDevice[] _devices;

        private readonly IRenderSurface[] _renderSurfaces;

        private readonly object _navigationLock = new object();
        private IMidiInputDevice _inputDevice;
        private NavigationLevel _navigationLevel;
        private int _activeKeyAnimation = -1;
        private IKeyAnimation[] _keyAnimations;
        private int _activeTransformAnimation = -1;
        private ITransformAnimation[] _transformAnimations;
        private PadState[] _padStates;
        private int _padTransitionStartIndex;

        private int _masterFader = 127;

        private Timer NavButtonBlinkTimer;

        private static Button[] _blinkButtons = new Button[]
        {
            Button.Zoom, Button.Select, Button.Right, Button.Left, Button.Down
        };
        private static int _blinkIndex = 0;

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

        public RenderService(
            IDmxDeviceEnumerator deviceEnumerator,
            IInputService inputService,
            IAudioService audioService
            )
        {
            _deviceEnumerator = deviceEnumerator;
            _inputService = inputService;
            _audioService = audioService;
            _renderThread = new Thread(RenderThreadLoop);
            _tcs = new TaskCompletionSource<bool>();
            _cts = new CancellationTokenSource();
            _renderSurfaces = new IRenderSurface[]
            {
                 AudioReactiveRenderSurface.Create(
                    _audioService,
                    new RenderSettings(255),
                    BetopperDJNew.Instance,
                    BetopperDJNew.Instance,
                    BetopperDJNew.Instance,
                    BetopperDJNew.Instance,
                    BetopperDJNew.Instance,
                    BetopperDJNew.Instance,
                    BetopperDJNew.Instance,
                    BetopperDJNew.Instance,
                    BetopperDJNew.Instance,
                    BetopperDJNew.Instance
                )
            };

            _devices = _renderSurfaces.SelectMany(r => r.GetDevices()).ToArray();
        }
        public RenderService(
            IDmxDeviceEnumerator deviceEnumerator,
            IInputService inputService,
            IAudioService audioService,
            Action<IDmxDevice[]> dmxDeviceAction
            ) : this(deviceEnumerator, inputService, audioService)
        {
            dmxDeviceAction(_devices);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _inputService.OnInputReceived += InputService_OnInputReceived;
            _inputService.OnInputDeviceReconnected += _inputService_OnInputDeviceReconnected;
            _inputDevice = _inputService.Devices.FirstOrDefault();
            _padTransitionStartIndex = _inputDevice.PadCount / 2;

            _outputDevices = _deviceEnumerator.GetDmxDevices(_cts.Token);
            _renderThread.Start();
            Initialize();
            NavButtonBlinkTimer = new Timer(Blink, this, 500, 500);
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
                }
                else if (navigationLevel == NavigationLevel.TransformAnimation && _activeTransformAnimation > -1)
                {
                    animation = _transformAnimations[_activeTransformAnimation];
                }
            }

            var deviceInitializationState = animation?.Start(_inputDevice.AsReadOnly());
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
                else if (e.EventType == DeviceInputEventType.RotaryEncoder)
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
            }
            else
            {
                IAnimation activeKeyAnimation = null;
                IAnimation activeTransformAnimation = null;
                lock (_navigationLock)
                {
                    if (_navigationLevel == NavigationLevel.KeyAnimation)
                    {
                        activeKeyAnimation = _activeKeyAnimation > -1 ? _keyAnimations[_activeKeyAnimation] : null;
                        activeTransformAnimation = _activeTransformAnimation > -1 ? _transformAnimations[_activeTransformAnimation] : null;
                    }
                }
                var midiAwareKeyAnimation = activeKeyAnimation as MidiAwareAnimation;
                midiAwareKeyAnimation?.MidiAware_OnMessageReceived(sender, e);

                var midiAwareTransformAnimation = activeTransformAnimation as MidiAwareAnimation;
                midiAwareTransformAnimation?.MidiAware_OnMessageReceived(sender, e);
            }
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
                Color.FromArgb(0xFF, 0xFF, 0x00, 0x00),
                Color.DarkRed,
                Color.OrangeRed,

                Color.FromArgb(0xFF, 0xFF, 0xA5, 0x00),
                Color.DarkOrange,

                Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00),
                Color.Gold,
                Color.DarkGoldenrod,

                Color.Green,
                Color.SeaGreen,

                Color.FromArgb(0xFF, 0x00, 0x00, 0xFF),

                Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF),
                Color.Purple,


                Color.Red,
                Color.DarkRed,
                Color.OrangeRed,
                Color.Gold,
                Color.Red,
                Color.DarkRed,
                Color.OrangeRed,
                Color.Gold,
                Color.Red,
                Color.DarkRed,
                Color.OrangeRed,
                Color.Gold,
                Color.Red,
                Color.DarkRed,
                Color.OrangeRed,
                Color.Gold,
                Color.Red,
                Color.DarkRed,
                Color.OrangeRed,
                Color.Gold,



                Color.OrangeRed,
                Color.Orange,
                Color.DarkOrange,
                Color.OrangeRed,
                Color.Orange,
                Color.DarkOrange,
                Color.OrangeRed,
                Color.Orange,
                Color.DarkOrange,
                Color.OrangeRed,
                Color.Orange,
                Color.DarkOrange,
                Color.OrangeRed,
                Color.Orange,
                Color.DarkOrange,

                Color.Gold,
                Color.Goldenrod,
                Color.DarkGoldenrod,
                Color.LightGoldenrodYellow,
                Color.PaleGoldenrod,
                Color.Gold,
                Color.Goldenrod,
                Color.DarkGoldenrod,
                Color.LightGoldenrodYellow,
                Color.PaleGoldenrod,
                Color.DarkGoldenrod,
                Color.LightGoldenrodYellow,
                Color.PaleGoldenrod,
                Color.LightGoldenrodYellow,
                Color.PaleGoldenrod,

                Color.Blue,
                Color.CornflowerBlue,
                Color.LightBlue,
                Color.DarkBlue,
                Color.DeepSkyBlue,
                Color.DarkSlateBlue,
                Color.RoyalBlue,
                Color.DeepSkyBlue,
                Color.DarkSlateBlue,
                Color.RoyalBlue,
                Color.Blue,

                Color.BlueViolet,
                Color.Purple,
                Color.DarkViolet,
                Color.BlueViolet,
                Color.Purple,
                Color.DarkViolet,
                Color.MediumVioletRed,
                Color.PaleVioletRed,
                Color.MediumVioletRed,
                Color.PaleVioletRed,
                Color.Purple,
                Color.DarkViolet,
                Color.MediumVioletRed,

                Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            };

            var space = new Color[]
            {
                Color.FromArgb(Convert.ToInt32("0xFFE91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xFFA52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFF5D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFFFF4DE9", 16)), // Pink
                Color.FromArgb(Convert.ToInt32("0xFF28C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow

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

                Color.FromArgb(Convert.ToInt32("0xFFE91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xFFEF32BF", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF910E5A", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFF13085", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFC2975", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFD21283", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow

                Color.FromArgb(Convert.ToInt32("0xFFE91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xFFEF32BF", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF910E5A", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFF13085", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFC2975", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFD21283", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow

                Color.FromArgb(Convert.ToInt32("0xFFE91893", 16)), // Redish-Pink
                Color.FromArgb(Convert.ToInt32("0xFFEF32BF", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF910E5A", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFF13085", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFFC2975", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFD21283", 16)),

                Color.FromArgb(Convert.ToInt32("0xFF28C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFF1BC8E2", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF02C3E4", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF05CBE5", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF50FDFC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF02BCDF", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFA52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFF9C236B", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF8D175B", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFCE2968", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFF7417A", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFFA52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFF9C236B", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF8D175B", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFCE2968", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFF7417A", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFA52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFF9C236B", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF8D175B", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFCE2968", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFF7417A", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFFA52668", 16)), // Red-Purple
                Color.FromArgb(Convert.ToInt32("0xFF9C236B", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF8D175B", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFCE2968", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFF7417A", 16)),

                Color.FromArgb(Convert.ToInt32("0xFF5D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0xFF641FA1", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF7033BC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF621D9D", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF7332BC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF6D2FBE", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0xFF5D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0xFF641FA1", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF7033BC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF621D9D", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF7332BC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF6D2FBE", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF5D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0xFF641FA1", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF7033BC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF621D9D", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF7332BC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF6D2FBE", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow
                Color.FromArgb(Convert.ToInt32("0xFF5D2099", 16)), // Purple
                Color.FromArgb(Convert.ToInt32("0xFF641FA1", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF7033BC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF621D9D", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF7332BC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF6D2FBE", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFDF7C87", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE98F99", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE87580", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE9537F", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFFF4DE9", 16)), // Pink

                Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFDF7C87", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE98F99", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE87580", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE9537F", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFFF4DE9", 16)), // Pink

                Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFDF7C87", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE98F99", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE87580", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE9537F", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
                Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFDF7C87", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE98F99", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE87580", 16)),
                Color.FromArgb(Convert.ToInt32("0xFFE9537F", 16)),

                Color.FromArgb(Convert.ToInt32("0xFF28C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFF1BC8E2", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF02C3E4", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF05CBE5", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF50FDFC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF02BCDF", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFFF4DE9", 16)), // Pink
                Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow

                Color.FromArgb(Convert.ToInt32("0xFF28C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFF1BC8E2", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF02C3E4", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF05CBE5", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF50FDFC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF02BCDF", 16)),

                Color.FromArgb(Convert.ToInt32("0xFFFF4DE9", 16)), // Pink
                Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow

                Color.FromArgb(Convert.ToInt32("0xFF28C8E1", 16)), // Blue
                Color.FromArgb(Convert.ToInt32("0xFF1BC8E2", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF02C3E4", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF05CBE5", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF50FDFC", 16)),
                Color.FromArgb(Convert.ToInt32("0xFF02BCDF", 16)),

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
            };


            var reds = new Color[]
            {
                Color.FromArgb(0xFF, 0xFF, 0x00, 0x00),
                Color.DarkRed,

                Color.FromArgb(0xFF, 0xFF, 0x18, 0x00),
                Color.FromArgb(0xFF, 0xE0, 0x00, 0x00),

                Color.FromArgb(0xFF, 0xFF, 0x16, 0x00),
            };

            _keyAnimations = new IKeyAnimation[] {
                new RainbowAnimation(_devices.Length, roygbiv, _audioService, _cts.Token),
                new RainbowAnimation(_devices.Length, warmColdRoygviv, _audioService, _cts.Token),
                //new VibrantAnimation(_devices.Length, _audioService, _cts.Token),
                //new ColorShimmerAnimation(_devices.Length, warmColdRoygviv, _audioService, _cts.Token),
                new RainbowAnimation(_devices.Length, space, _audioService, _cts.Token),
                //new ColorPaletteAnimation(_devices.Length, _audioService, _cts.Token),
            };

            _transformAnimations = new ITransformAnimation[]
            {
                new MirrorTransformAnimation(),
                new WaveDim(),
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
            byte[] targetVector = new byte[512];
            var renderSettings = new RenderSettings();
            while (!_cts.Token.IsCancellationRequested)
            {
                renderSettings.MasterFader = _masterFader.InterlockedGet();
                foreach (IDmxOutputDevice outputDevice in _outputDevices)
                {
                    IKeyAnimation keyAnimation = null;
                    ITransformAnimation transformAnimation = null;
                    lock (_navigationLock)
                    {
                        keyAnimation = _activeKeyAnimation > -1 ? _keyAnimations[_activeKeyAnimation] : null;
                        transformAnimation = _activeTransformAnimation > -1 ? _transformAnimations[_activeTransformAnimation] : null;
                    }
                    if (keyAnimation != null)
                    {
                        var frame = keyAnimation.GetNextFrame();
                        if (transformAnimation != null)
                        {
                            frame = transformAnimation.GetNextFrame(frame);
                        }
                        int pixelOffset = 0;
                        int channelOffset = 0;
                        var frameSpan = frame.AsSpan();
                        for (int i = 0; i < _renderSurfaces.Length; i++)
                        {
                            var surface = _renderSurfaces[i];
                            
                            surface.Render(frameSpan.Slice(pixelOffset, surface.TotalPixelCount), targetVector.AsSpan(), renderSettings);
                            pixelOffset += surface.TotalPixelCount;
                            channelOffset += surface.TotalChannelCount;
                        }
                    }

                    outputDevice.Write(targetVector);
                }
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
