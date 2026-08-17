using ArtNetSharp.Communication;
using ArtNetSharp;
using ShowControl.Animation;
using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
using ShowControl.Devices.Input;
using ShowControl.Devices.Input.Extensions;
using ShowControl.Devices.Output;
using ShowControl.Graphics;
using ShowControl.PixelMapping;
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
        private OutputDeviceHealth[] _outputDeviceHealth;
        private Task[] _writeTasks;
        // Reused per frame so slicing the target vector per device costs no allocation.
        private byte[][] _deviceBuffers;
        private readonly OutputDeviceHealth _artNetSyncHealth = new OutputDeviceHealth();
        private readonly Thread _renderThread;
        private readonly CancellationTokenSource _cts;
        private readonly TaskCompletionSource<bool> _tcs;
        private readonly IDmxDeviceEnumerator _deviceEnumerator;
        private readonly IInputService _inputService;
        private readonly IAudioService _audioService;
        private readonly IGraphicsHost _graphicsHost;

        private readonly IPixelMapSource _pixelMapSource;
        private readonly PixelTypeRegistry _pixelTypes = PixelTypeRegistry.Default;
        // Written by the UI thread, consumed by the render thread at a frame boundary. The render
        // thread is the only writer of everything it rebuilds, so this reference is the whole
        // synchronization story for a structural change.
        private PixelMapLayout _pendingLayout;
        private PixelMapLayout _activeLayout;
        private byte[] _targetVector;
        private bool _loggedFrameSizeMismatch;

        private IRenderSurface _pixelChannels;
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
                if (_activeKeyAnimation > -1 && _keyAnimations[_activeKeyAnimation] is ShaderAnimation shaderAnimation)
                {
                    shaderAnimation.NudgeParamsTowardDefaults();
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
            IPixelMapSource pixelMapSource,
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
            _pixelMapSource = pixelMapSource;
            _audioService.AudioFrameProcessed += _audioService_AudioFrameProcessed;
            _renderThread = new Thread(RenderThreadLoop);
            _tcs = new TaskCompletionSource<bool>();
            _cts = new CancellationTokenSource();

            // The pixel surface is built from the saved map, not from a hardcoded pixel count.
            _activeLayout = _pixelMapSource.CurrentLayout;
            _pixelChannels = PixelMapSurfaceBuilder.Build(_activeLayout, _pixelTypes, new RenderSettings(255));

            if (_pixelMapSource is IPixelMapEditor editor)
            {
                editor.Changed += PixelMap_Changed;
            }

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
            _targetVector = new byte[_pixelChannels.TotalChannelCount + _parChannels.TotalChannelCount];
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
            IPixelMapSource pixelMapSource,
            Action<PixelMapSummary> pixelMapSummaryAction,
            ILogger<RenderService> logger,
            IArtNetController artNetController
            ) : this(deviceEnumerator, inputService, graphicsHost, audioService, pixelMapSource, logger, artNetController)
        {
            pixelMapSummaryAction(_pixelMapSource.CurrentSummary);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RenderService starting");
            _inputService.OnInputReceived += InputService_OnInputReceived;
            _inputService.OnInputDeviceReconnected += _inputService_OnInputDeviceReconnected;

            // No control surface is not a startup failure. Everything the show actually needs —
            // shader, pixel map, DMX output — is independent of it, and the host is started from the
            // MainPage constructor, so throwing here takes the whole app down before it has a window.
            _inputDevice = _inputService.Devices.FirstOrDefault() ?? AbsentMidiInputDevice.Instance;
            if (ReferenceEquals(_inputDevice, AbsentMidiInputDevice.Instance))
            {
                _logger.LogWarning("No control surface found; running without one. Plugging one in is picked up live.");
            }
            else
            {
                _logger.LogInformation("Input device: {InputDevice}", _inputDevice);
            }

            _padTransitionStartIndex = _inputDevice.PadCount / 2;
            RebuildOutputDevices(_activeLayout, previousLayout: null, previousDevices: null);
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

        /// <summary>
        /// Picks up a control surface that appeared after startup. On Mac the CoreMIDI notification
        /// fires for a surface plugged in later as well as for one that reconnected, so a show that
        /// came up without one adopts it here rather than staying dark until the next launch.
        /// </summary>
        private void _inputService_OnInputDeviceReconnected(object sender, EventArgs e)
        {
            lock (_navigationLock)
            {
                if (ReferenceEquals(_inputDevice, AbsentMidiInputDevice.Instance))
                {
                    var device = _inputService.Devices.FirstOrDefault();
                    if (device != null)
                    {
                        _inputDevice = device;
                        _padTransitionStartIndex = _inputDevice.PadCount / 2;
                        if (_keyAnimations != null)
                        {
                            InitializePads();
                        }
                        _logger.LogInformation("Control surface connected: {InputDevice}", _inputDevice);
                    }
                }

                SetNavigationLevel(_navigationLevel);
            }
        }

        /// <summary>
        /// Pad slots only exist while a control surface is connected. With none the array is empty,
        /// so every pad write has to be a no-op rather than an index out of range — some of these
        /// calls arrive on an animation's own timer thread, where it would be fatal.
        /// </summary>
        private void SetPadIllumination(int index, PadIllumination illumination)
        {
            if (_padStates != null && index >= 0 && index < _padStates.Length)
            {
                _padStates[index].PadIllumination = illumination;
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
                    SetPadIllumination(_activeKeyAnimation, PadIllumination.Solid);
                    var existingAnimation = _keyAnimations[_activeKeyAnimation];
                    existingAnimation.Stop();
                    existingAnimation = _auxilaryKeyAnimations[_activeKeyAnimation];
                    existingAnimation.Stop();
                }
                SetPadIllumination(index, PadIllumination.Blink);
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
                    SetPadIllumination(index + _padTransitionStartIndex, PadIllumination.Solid);
                    animation = _transformAnimations[_activeTransformAnimation];
                    animation.Stop();
                    _activeTransformAnimation = -1;
                    return false;
                }
                else if (_activeTransformAnimation > -1)
                {
                    SetPadIllumination(_activeTransformAnimation + _padTransitionStartIndex, PadIllumination.Solid);
                    var existingAnimation = _transformAnimations[_activeTransformAnimation];
                    existingAnimation.Stop();
                }

                SetPadIllumination(index + _padTransitionStartIndex, PadIllumination.Breathe);
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

        /// <summary>
        /// Turns a send failure into at most one log line per outage, plus a periodic
        /// reminder while it persists. Called from the render loop, so it must stay
        /// silent in the common (already-reported) case.
        /// </summary>
        private void ReportOutputDeviceFailure(int index, Exception exception)
        {
            var transition = _outputDeviceHealth[index].RecordFailure(_frameRateStopWatch.Elapsed);
            switch (transition.Action)
            {
                case OutputDeviceLogAction.WentDown:
                    _logger.LogWarning(exception,
                        "Output device {Device} send failed; retrying every {RetrySeconds:F0}s, further failures suppressed until it recovers.",
                        _outputDevices[index], OutputDeviceHealth.DefaultRetryInterval.TotalSeconds);
                    break;
                case OutputDeviceLogAction.StillDown:
                    _logger.LogWarning(
                        "Output device {Device} still failing: {FailedAttempts} attempts over {DownSeconds:F0}s.",
                        _outputDevices[index], transition.FailedAttempts, transition.DownFor.TotalSeconds);
                    break;
            }
        }

        private void ReportOutputDeviceSuccess(int index)
        {
            var transition = _outputDeviceHealth[index].RecordSuccess(_frameRateStopWatch.Elapsed);
            if (transition.Action == OutputDeviceLogAction.Recovered)
            {
                _logger.LogInformation(
                    "Output device {Device} resumed after {FailedAttempts} failed attempts over {DownSeconds:F0}s.",
                    _outputDevices[index], transition.FailedAttempts, transition.DownFor.TotalSeconds);
            }
        }

        private void ReportArtNetSyncFailure(Exception exception)
        {
            var transition = _artNetSyncHealth.RecordFailure(_frameRateStopWatch.Elapsed);
            switch (transition.Action)
            {
                case OutputDeviceLogAction.WentDown:
                    _logger.LogWarning(exception, "ArtNet sync failed; further failures suppressed until it recovers.");
                    break;
                case OutputDeviceLogAction.StillDown:
                    _logger.LogWarning("ArtNet sync still failing: {FailedAttempts} attempts over {DownSeconds:F0}s.",
                        transition.FailedAttempts, transition.DownFor.TotalSeconds);
                    break;
            }
        }

        /// <summary>
        /// Logs the rig one line per fixture. Under the old arrangement every pixel was its own
        /// IDmxDevice, so this had to run-length compress ~11.5k entries; the map gives it real
        /// fixtures to name instead.
        /// </summary>
        private void LogPixelMapSummary()
        {
            var summary = _pixelMapSource.CurrentSummary;
            _logger.LogInformation(
                "Pixel map: {Pixels} pixels, {Channels} channels, {Outputs} output device(s).",
                summary.TotalPixelCount, summary.TotalChannelCount, summary.OutputCount);

            foreach (var line in summary.Lines)
            {
                _logger.LogInformation("  {Fixture}", line.ToString());
            }
        }

        /// <summary>
        /// Applies an edit made in the pixel map editor while the show is running.
        /// <para>
        /// A geometry change is only new sample offsets, so it is published straight to the shader
        /// animation with a volatile write — the interactive drag path, which must stay smooth.
        /// A structural change needs new buffers, surfaces and devices, so it is handed to the
        /// render thread to perform itself at a frame boundary.
        /// </para>
        /// </summary>
        private void PixelMap_Changed(object sender, PixelMapChangedEventArgs e)
        {
            if (e.Kind == PixelMapChangeKind.Geometry)
            {
                lock (_navigationLock)
                {
                    if (_keyAnimations != null)
                    {
                        foreach (var animation in _keyAnimations)
                        {
                            if (animation is ShaderAnimation shaderAnimation)
                            {
                                shaderAnimation.SetSampleMap(e.SampleMap);
                            }
                        }
                    }
                }
                return;
            }

            Volatile.Write(ref _pendingLayout, e.Layout);
        }

        /// <summary>
        /// Rebuilds everything sized by the map. Runs on the render thread between frames, so the
        /// render thread stays the only writer of every field it reads and nothing here has to be
        /// synchronized beyond the pending-layout reference.
        /// <para>
        /// Reverts wholesale on failure: a half-applied rebuild would trip the pixel-count check in
        /// RenderSurface.Render, which throws out of the render loop and leaves the rig frozen.
        /// </para>
        /// </summary>
        private void ApplyPendingLayout(PixelMapLayout layout)
        {
            var previousLayout = _activeLayout;
            var previousSurface = _pixelChannels;
            var previousTargetVector = _targetVector;
            var previousDevices = _outputDevices;
            var previousHealth = _outputDeviceHealth;
            var previousWriteTasks = _writeTasks;
            var previousBuffers = _deviceBuffers;

            try
            {
                _pixelChannels = PixelMapSurfaceBuilder.Build(layout, _pixelTypes, new RenderSettings(255));
                _targetVector = new byte[_pixelChannels.TotalChannelCount + _parChannels.TotalChannelCount];
                _activeLayout = layout;
                RebuildOutputDevices(layout, previousLayout, previousDevices);

                lock (_navigationLock)
                {
                    if (_keyAnimations != null)
                    {
                        foreach (var animation in _keyAnimations)
                        {
                            // Never reconstruct the animation: Start() calls SetShader, and a
                            // pipeline rebuild at runtime can hang the GPU on MoltenVK.
                            if (animation is ShaderAnimation shaderAnimation)
                            {
                                shaderAnimation.SetSampleMap(_pixelMapSource.CurrentSampleMap);
                            }
                        }
                    }
                }

                _logger.LogInformation(
                    "Pixel map applied: {Pixels} pixels, {Channels} channels, {Outputs} output device(s).",
                    layout.TotalPixelCount, layout.TotalChannelCount, layout.Outputs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not apply the edited pixel map; keeping the previous one.");
                _activeLayout = previousLayout;
                _pixelChannels = previousSurface;
                _targetVector = previousTargetVector;
                _outputDevices = previousDevices;
                _outputDeviceHealth = previousHealth;
                _writeTasks = previousWriteTasks;
                _deviceBuffers = previousBuffers;
            }
        }

        /// <summary>
        /// Swaps in the device list for a new layout, keeping any device whose endpoint and channel
        /// count are unchanged. Adding a fixture to one controller must not blank the others while
        /// their sockets are torn down and reopened.
        /// </summary>
        private void RebuildOutputDevices(PixelMapLayout layout, PixelMapLayout previousLayout, IDmxOutputDevice[] previousDevices)
        {
            var rebuilt = _deviceEnumerator.GetDmxDevices(_cts.Token);
            if (rebuilt.Length != layout.Outputs.Count)
            {
                throw new InvalidOperationException(
                    $"Device enumerator returned {rebuilt.Length} devices for a layout with {layout.Outputs.Count} outputs.");
            }

            var devices = new IDmxOutputDevice[rebuilt.Length];
            var health = new OutputDeviceHealth[rebuilt.Length];
            var buffers = new byte[rebuilt.Length][];
            var reused = new bool[previousDevices?.Length ?? 0];

            for (int i = 0; i < rebuilt.Length; i++)
            {
                int carriedOver = -1;
                if (previousDevices != null && previousLayout != null)
                {
                    for (int j = 0; j < previousLayout.Outputs.Count && j < previousDevices.Length; j++)
                    {
                        if (!reused[j] &&
                            previousLayout.Outputs[j].Target.Equals(layout.Outputs[i].Target) &&
                            previousLayout.Outputs[j].ChannelCount == layout.Outputs[i].ChannelCount)
                        {
                            carriedOver = j;
                            break;
                        }
                    }
                }

                if (carriedOver >= 0)
                {
                    reused[carriedOver] = true;
                    devices[i] = previousDevices[carriedOver];
                    health[i] = _outputDeviceHealth != null && carriedOver < _outputDeviceHealth.Length
                        ? _outputDeviceHealth[carriedOver]
                        : new OutputDeviceHealth();
                    (rebuilt[i] as IDisposable)?.Dispose();
                }
                else
                {
                    devices[i] = rebuilt[i];
                    health[i] = new OutputDeviceHealth();
                    _logger.LogInformation("Output device: {Device}", devices[i]);
                }

                buffers[i] = new byte[layout.Outputs[i].ChannelCount];
            }

            if (previousDevices != null)
            {
                for (int j = 0; j < previousDevices.Length; j++)
                {
                    if (!reused[j])
                    {
                        _logger.LogInformation("Output device removed: {Device}", previousDevices[j]);
                        (previousDevices[j] as IDisposable)?.Dispose();
                    }
                }
            }

            _outputDevices = devices;
            _outputDeviceHealth = health;
            _writeTasks = new Task[devices.Length];
            _deviceBuffers = buffers;
        }

        private void Initialize()
        {
            LogPixelMapSummary();


            _keyAnimations = new IKeyAnimation[]
            {
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, plc, _audioService, _cts.Token, fastAnimate: true),
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, nochesCincoDeMayo1Long, _audioService, _cts.Token, fastAnimate: true),
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, dragChurch, _audioService, _cts.Token, fastAnimate: true),
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, warmLucidas, _audioService, _cts.Token, fastAnimate: true),
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, dragonCyborg9, _audioService, _cts.Token, fastAnimate: true),
                //new RainbowAnimation(_pixelChannels.TotalPixelCount, dragonCyborg2Lucidas, _audioService, _cts.Token, fastAnimate: true),
                // new ShaderAnimation(_pixelMapSource.CurrentSampleMap, _graphicsHost, Shaders.Geometry, new Color[]
                // {
                //     Color.Red,
                //     Color.FromArgb(255, 0, 137),
                //     Color.FromArgb(255, 0, 35),
                //     Color.FromArgb(67, 112, 0),
                //     Color.FromArgb(255, 74, 0),
                //     Color.FromArgb(9, 254, 182)
                // }),
                new ShaderAnimation(_pixelMapSource.CurrentSampleMap, _graphicsHost, Shaders.Kaleidoscope, frequentWarmColdRoygviv, _logger),
                // new ShaderAnimation(_pixelMapSource.CurrentSampleMap, _graphicsHost, Shaders.Clown, new Color[]
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
                // })
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
                //new ShaderAnimation(_pixelMapSource.CurrentSampleMap, _graphicsHost, Shaders.Kaleidoscope, dragonCyborg9)
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
            _masterFader = FaderSchedule.Evaluate(_faderSchedule, FaderSchedule.ToScheduleHour(DateTime.Now));
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
                // Structural pixel-map edits are applied here, before anything reads the buffers
                // they resize. Costs one frame; structural edits are rare and a single dark frame
                // at ~46fps is invisible.
                var pendingLayout = Volatile.Read(ref _pendingLayout);
                if (pendingLayout != null)
                {
                    Volatile.Write(ref _pendingLayout, null);
                    ApplyPendingLayout(pendingLayout);
                }

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
                var targetVector = _targetVector;
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

                    // RenderSurface.Render throws on a size mismatch, and an exception here exits
                    // the loop for good and leaves the rig frozen. Skip the render instead — but
                    // still fall through to the frame pacing below, or a persistent mismatch would
                    // spin the render thread at full speed.
                    if (frameSpan.Length != surface.TotalPixelCount)
                    {
                        if (!_loggedFrameSizeMismatch)
                        {
                            _loggedFrameSizeMismatch = true;
                            _logger.LogWarning(
                                "Animation produced {FrameLength} pixels but the surface expects {SurfaceLength}; not rendering until they agree.",
                                frameSpan.Length, surface.TotalPixelCount);
                        }
                    }
                    else
                    {
                        _loggedFrameSizeMismatch = false;
                        surface.Render(frameSpan.Slice(pixelOffset, surface.TotalPixelCount), targetVector.AsSpan().Slice(channelOffset, surface.TotalChannelCount), renderSettings);
                        pixelOffset += surface.TotalPixelCount;
                        channelOffset += surface.TotalChannelCount;
                    }
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

                // Dispatch every device before awaiting any of them, so the writes still
                // overlap. Each device is isolated: one unreachable controller must not
                // stop the others from being written, and a device that keeps failing
                // backs off instead of throwing (and logging) on every frame.
                // Each device's channel range comes from the layout rather than from a running
                // total over the device array, so enabling a fixture can no longer shift another
                // controller's data.
                for (int i = 0; i < _outputDevices.Length; i++)
                {
                    var outputDevice = _outputDevices[i];
                    var segment = _activeLayout.Outputs[i];

                    _writeTasks[i] = null;
                    if (!_outputDeviceHealth[i].ShouldAttempt(_frameRateStopWatch.Elapsed))
                    {
                        continue;
                    }

                    try
                    {
                        var buffer = _deviceBuffers[i];
                        Array.Copy(targetVector, segment.ChannelStart, buffer, 0, segment.ChannelCount);
                        _writeTasks[i] = outputDevice.Write(buffer);
                    }
                    catch (Exception ex)
                    {
                        ReportOutputDeviceFailure(i, ex);
                    }
                }

                for (int i = 0; i < _writeTasks.Length; i++)
                {
                    var writeTask = _writeTasks[i];
                    if (writeTask == null)
                    {
                        continue;
                    }
                    _writeTasks[i] = null;

                    try
                    {
                        writeTask.GetAwaiter().GetResult();
                        ReportOutputDeviceSuccess(i);
                    }
                    catch (Exception ex)
                    {
                        ReportOutputDeviceFailure(i, ex);
                    }
                }

                if (_artNetSyncHealth.ShouldAttempt(_frameRateStopWatch.Elapsed))
                {
                    try
                    {
                        _artNetController.SendSync().GetAwaiter().GetResult();
                        if (_artNetSyncHealth.RecordSuccess(_frameRateStopWatch.Elapsed).Action == OutputDeviceLogAction.Recovered)
                        {
                            _logger.LogInformation("ArtNet sync resumed.");
                        }
                    }
                    catch (Exception ex)
                    {
                        ReportArtNetSyncFailure(ex);
                    }
                }

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
                // Raised from the animation's own timer thread, and with no control surface there
                // are no pad slots to colour, so the bounds check is what keeps that thread alive.
                var padStates = _padStates;
                if (sourceAnimationIndex > -1 && padStates != null && sourceAnimationIndex < padStates.Length)
                {
                    padStates[sourceAnimationIndex].Color = e.NavigationColor;
                    if (navigationLevel == NavigationLevel.Root)
                    {
                        _inputDevice.SetPadState(sourceAnimationIndex, padStates[sourceAnimationIndex]);
                    }
                }
            }
        }
    }
}
