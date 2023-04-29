using ShowControl.Animation;
using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
using ShowControl.Devices.Input;
using ShowControl.Devices.Input.Extensions;
using ShowControl.Devices.Output;
using ShowControl.Utilities;
using System;
using System.Collections.Generic;
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

        private readonly object _navigationAnimationLock = new object();
        private IMidiInputDevice _inputDevice;
        private NavigationLevel _navigationLevel;
        private int _activeKeyAnimation = -1;
        private IKeyAnimation[] _keyAnimations;
        private int _activeTransformAnimation = -1;
        private ITransformAnimation[] _transformAnimations;
        private PadState[] _padStates;
        private int _padTransitionStartIndex;

        private int _masterFader = 255;

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
            _inputDevice = _inputService.Devices.FirstOrDefault();
            _padTransitionStartIndex = _inputDevice.PadCount / 2;

            _outputDevices = _deviceEnumerator.GetDmxDevices(_cts.Token);
            _renderThread.Start();
            Initialize();
            return Task.CompletedTask;
        }

        private bool SetActiveKeyAnimation(int index)
        {
            IKeyAnimation animation;
            lock (_navigationAnimationLock)
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
            lock (_navigationAnimationLock)
            {
                if (index >= _transformAnimations.Length)
                {
                    return false;
                }
                if (_activeTransformAnimation > -1)
                {
                    _padStates[_activeTransformAnimation + _padTransitionStartIndex].PadIllumination = PadIllumination.Solid;
                    var existingAnimation = _transformAnimations[_activeTransformAnimation];
                    existingAnimation.Stop();
                }

                if (_activeTransformAnimation == index)
                {
                    _padStates[index + _padTransitionStartIndex].PadIllumination = PadIllumination.Solid;
                    _activeTransformAnimation = -1;
                    animation = null;
                    return false;
                }
                _padStates[index + _padTransitionStartIndex].PadIllumination = PadIllumination.Breathe;
                _activeTransformAnimation = index;
                animation = _transformAnimations[_activeTransformAnimation];
            }
            return false;
        }

        private void SetNavigationLevel(NavigationLevel navigationLevel)
        {
            IAnimation animation = null;
            lock (_navigationAnimationLock)
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
            if (e.EventType == DeviceInputEventType.Navigation)
            {
                ProcessNavigationButtonPressed(e.ButtonDirection, e.NavigationDirection);
            }
            else if (_navigationLevel == NavigationLevel.Root)
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
                IAnimation animation = null;
                lock (_navigationAnimationLock)
                {
                    if (_navigationLevel == NavigationLevel.KeyAnimation)
                    {
                        animation = _activeKeyAnimation > -1 ? _keyAnimations[_activeKeyAnimation] : null;
                    }
                    else if (_navigationLevel == NavigationLevel.TransformAnimation)
                    {
                        animation = _activeTransformAnimation > -1 ? _transformAnimations[_activeTransformAnimation] : null;
                    }
                }
                var midiAware = animation as MidiAwareAnimation;
                midiAware?.MidiAware_OnMessageReceived(sender, e);
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
                _inputDevice.SetPadState(padNumber, _padStates[padNumber]);
            }
        }

        private void ProcessNavigationButtonPressed(ButtonDirection direction, NavigationButton navigationButton)
        {
            if (direction == ButtonDirection.Press && navigationButton == NavigationButton.Up)
            {
                _inputDevice.ClearInterface();
                SetNavigationLevel(_navigationLevel == NavigationLevel.Root ? NavigationLevel.KeyAnimation : NavigationLevel.Root);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cts.Cancel();
            _inputService.Stop();
            _audioService.Dispose();
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

            _keyAnimations = new IKeyAnimation[] {
                new RainbowAnimation(_devices.Length, _audioService),
                new VibrantAnimation(_devices.Length, _audioService, _cts.Token)
            };

            _transformAnimations = new ITransformAnimation[]
            {
                new MirrorTransformAnimation()
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
            //List<byte> bytes = new List<byte>(512);
            byte[] targetVector = new byte[512];
            var renderSettings = new RenderSettings();
            while (!_cts.Token.IsCancellationRequested)
            {
                renderSettings.MasterFader = _masterFader.InterlockedGet();
                foreach (IDmxOutputDevice outputDevice in _outputDevices)
                {
                    //bytes.Clear();
                    int expectedFrameLength = 0;
                    IKeyAnimation keyAnimation = null;
                    ITransformAnimation transformAnimation = null;
                    lock (_navigationAnimationLock)
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
                        //for (int i = 0; i < frame.Length; i++)
                        //{
                        //    var device = _devices[i];
                        //    device.Color = frame[i].Color;
                        //    device.Mode = frame[i].SoundReactivity > 0 ? DmxDeviceMode.SoundReactive : DmxDeviceMode.RGB;
                        //    if (device.Mode == DmxDeviceMode.SoundReactive)
                        //    {
                        //        device.SoundReactivitySensitivity = frame[i].SoundReactivity;
                        //    }
                        //    device.MasterFader = (byte)_masterFader.InterlockedGet();
                        //    bytes.AddRange(device.GetDMXVector());
                        //    expectedFrameLength += device.ChannelCount;
                        //    if (bytes.Count != expectedFrameLength)
                        //    {
                        //        throw new Exception("Previous device did not return expected number of channels");
                        //    }
                        //}
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
            lock (_navigationAnimationLock)
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
