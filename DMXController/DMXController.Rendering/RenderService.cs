using ShowControl.Animation;
using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
using ShowControl.Devices.Input;
using ShowControl.Devices.Input.Extensions;
using ShowControl.Devices.Output;
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
        private enum NavigationLevel { Root, Animation};
        private IDmxOutputDevice[] _outputDevices;
        private Thread _renderThread;
        private long _renderLoopContinue = 1;
        private CancellationTokenSource _tokenSource;
        private readonly TaskCompletionSource<bool> _tcs;
        private IDmxDeviceEnumerator _deviceEnumerator;
        private IInputService _inputService;
        IDmxDevice[] _devices;

        private readonly object _navigationAnimationLock = new object();
        private IMidiInputDevice _inputDevice;
        private NavigationLevel _navigationLevel;
        private int _activeAnimation = -1;
        private MidiAwareAnimation[] _animations;
        private System.Drawing.Color[] _padColors;
        public RenderService(
            IDmxDeviceEnumerator deviceEnumerator,
            IInputService inputService
            /*IAudioService audioService*/)
        {
            _deviceEnumerator = deviceEnumerator;
            _inputService = inputService;
            _renderThread = new Thread(RenderThreadLoop);
            _tcs = new TaskCompletionSource<bool>();
            _tokenSource = new CancellationTokenSource();
            _devices = new IDmxDevice[]
            {
                new Missyee(),
                new Missyee(),
                new Missyee(),
                new Missyee(),
                new Missyee(),
                new Missyee(),
                new Missyee(),
                new Missyee(),
                new JLPow(),
                new JLPow(),
                new JLPow(),
                new JLPow(),
                new JLPow(),
                new JLPow(),
                new BetopperDJ(),
                new BetopperDJ()
            };
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _inputService.OnInputReceived += InputService_OnInputReceived;
            _inputDevice = _inputService.Devices.FirstOrDefault();
            _outputDevices = _deviceEnumerator.GetDmxDevices(_tokenSource.Token);
            _renderThread.Start();
            Initialize();
            return Task.CompletedTask;
        }

        private void SetActiveAnimation(int index)
        {
            MidiAwareAnimation animation;
            lock (_navigationAnimationLock)
            {
                if (index >= _animations.Length)
                {
                    return;
                }
                if (_activeAnimation > -1)
                {
                    var existingAnimation = _animations[_activeAnimation];
                    existingAnimation.Stop();
                }
                _activeAnimation = index;
                animation = _animations[_activeAnimation];
            }
        }

        private void SetNavigationLevel(NavigationLevel navigationLevel)
        {
            MidiAwareAnimation animation = null;
            lock (_navigationAnimationLock)
            {
                _navigationLevel = navigationLevel;

                if (navigationLevel == NavigationLevel.Root)
                {
                    _inputDevice.ClearInterface();
                    _inputDevice.SetButtonIllumination(Button.Up, LightState.On);
                    for (int i = 0; i < _animations.Length; i++)
                    {
                        _inputDevice.SetPadColor(i, _padColors[i]);
                    }
                }
                else if (_activeAnimation > -1)
                {
                    animation = _animations[_activeAnimation];
                }
            }
            var deviceInitializationState = animation?.Start(_inputDevice.AsReadOnly());
            if (deviceInitializationState?.PadColors != null && deviceInitializationState.PadColors.Length > 0)
            {
                _inputDevice.SetPadColors(deviceInitializationState.PadColors);
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
                if (e.ButtonDirection == ButtonDirection.Press && e.NavigationDirection == NavigationButton.Up)
                {
                    _inputDevice.ClearInterface();
                    SetNavigationLevel(_navigationLevel == NavigationLevel.Root ? NavigationLevel.Animation : NavigationLevel.Root);
                }
            }
            else if (_navigationLevel == NavigationLevel.Root)
            {
                if (e.EventType == DeviceInputEventType.Pad && e.ButtonDirection == ButtonDirection.Press)
                {
                    _inputDevice.ClearInterface();
                    SetActiveAnimation(e.PadNumber.Value);
                    SetNavigationLevel(NavigationLevel.Animation);
                }
                else if (e.EventType == DeviceInputEventType.Pad && e.ButtonDirection == ButtonDirection.Release)
                {
                    _inputDevice.SetPadColor(e.PadNumber.Value, System.Drawing.Color.Red);
                }
            }
            else if (_navigationLevel == NavigationLevel.Animation)
            {
                MidiAwareAnimation animation = null;
                lock (_navigationAnimationLock)
                {
                    animation = _activeAnimation > -1 ? _animations[_activeAnimation] : null;
                }
                animation?.MidiAware_OnMessageReceived(sender, e);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref _renderLoopContinue, 0);
            _tokenSource.Cancel();
            return _tcs.Task;
        }

        private void Initialize()
        {
            int address = 1;
            for (int j = 0; j < _devices.Length; j++)
            {
                Console.WriteLine("Address {0} for {1}: {2}", j + 1, _devices[j].GetType().ToString(), address);
                address += _devices[j].ChannelCount;
            }

            _animations = new MidiAwareAnimation[] {
                new RainbowAnimation(_devices.Length),
                new ColorMixAnimation(_devices.Length)
            };

            _padColors = new System.Drawing.Color[_animations.Length];
            Array.Fill(_padColors, System.Drawing.Color.Red);

            SetNavigationLevel(NavigationLevel.Root);

            for (int i = 0; i < _animations.Length; i++)
            {
                var animation = _animations[i];
                animation.MidiAware_DeviceStateChangeEvent += MidiAware_OnDeviceStateChange;
            }
        }

        private void RenderThreadLoop()
        {
            while (Interlocked.Read(ref _renderLoopContinue) == 1 && !_tokenSource.Token.IsCancellationRequested)
            {
                foreach (IDmxOutputDevice outputDevice in _outputDevices)
                {
                    List<byte> bytes = new List<byte>(512);
                    int expectedFrameLength = 0;
                    IAnimation animation = null;
                    lock (_navigationAnimationLock)
                    {
                        animation = _activeAnimation > -1 ? _animations[_activeAnimation] : null;
                    }
                    if (animation != null)
                    {
                        var frame = animation.GetNextFrame();
                        for (int i = 0; i < frame.Length; i++)
                        {
                            var device = _devices[i];
                            device.Color = frame[i].Color;
                            device.Mode = frame[i].SoundReactivity > 0 ? DmxDeviceMode.SoundReactive : DmxDeviceMode.RGB;
                            if (device.Mode == DmxDeviceMode.SoundReactive)
                            {
                                device.SoundReactivitySensitivity = frame[i].SoundReactivity;
                            }
                            bytes.AddRange(device.GetDMXVector());
                            expectedFrameLength += device.ChannelCount;
                            if (bytes.Count != expectedFrameLength)
                            {
                                throw new Exception("Previous device did not return expected number of channels");
                            }
                        }
                    }

                    outputDevice.Write(bytes.ToArray());
                }
                Thread.Sleep(1);
            }
            _tcs.SetResult(true);
        }

        private void MidiAware_OnDeviceStateChange(object sender, DeviceStateChangeEventArgs e)
        {
            MidiAwareAnimation activeAnimation = null;
            NavigationLevel navigationLevel;
            int sourceAnimationIndex = -1;
            lock (_navigationAnimationLock)
            {
                if (_activeAnimation > -1)
                {
                    activeAnimation = _animations[_activeAnimation];
                }
                for (int i = 0; i < _animations.Length; i++)
                {
                    if (sender == _animations[i])
                    {
                        sourceAnimationIndex = i;
                    }
                }
                navigationLevel = _navigationLevel;
            }
            if (sender == activeAnimation && e.EventType == DeviceStateChangeEventType.PadColor)
            {
                _inputDevice.SetPadColor(e.PadNumber, e.PadColor);
            }
            else if (e.EventType == DeviceStateChangeEventType.NavigationColor)
            {
                if (sourceAnimationIndex > -1)
                {
                    _padColors[sourceAnimationIndex] = e.NavigationColor;
                    if (navigationLevel == NavigationLevel.Root)
                    {
                        _inputDevice.SetPadColor(sourceAnimationIndex, _padColors[sourceAnimationIndex]);
                    }
                }
            }
        }
    }
}
