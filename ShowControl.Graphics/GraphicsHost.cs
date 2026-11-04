using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO;
using Silk.NET.Vulkan.Extensions.EXT;
using Device = Silk.NET.Vulkan.Device;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Image = Silk.NET.Vulkan.Image;
using System.Xml.Linq;
using Buffer = Silk.NET.Vulkan.Buffer;
using System.Reflection.Metadata.Ecma335;
using Microsoft.Maui.Graphics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShowControl.Utilities;
using System.Threading;
using System.Threading.Tasks;

#if MACCATALYST
using Microsoft.Maui.ApplicationModel;
using UIKit;
using CoreGraphics;
using CoreAnimation;
using Foundation;
using ObjCRuntime;
#elif WINDOWS
using ShowControl.Graphics.Platforms.Windows;
#endif


namespace ShowControl.Graphics
{
    public class GraphicsHost : IGraphicsHost
    {
        private static Stream OpenShaderFile(string filename)
        {
            try
            {
                return FileSystem.OpenAppPackageFileAsync(filename).Result;
            }
            catch
            {
                return File.OpenRead(filename);
            }
        }

        // Matches the index-0 startup animation in RenderService, so the initial pipeline
        // is built for the shader that will actually run (the same-name SetShader at
        // startup then follows the identical path kaleidoscope always took).
        private string _shader = Shaders.Fluid;
        private Thread _backgroundThread;
        private readonly ILogger<GraphicsHost> _logger;
        // static handle for the [UnmanagedCallersOnly] Vulkan debug callback
        private static ILogger<GraphicsHost> _staticLogger;

        public GraphicsHost() : this(NullLogger<GraphicsHost>.Instance)
        {
        }

        public GraphicsHost(ILogger<GraphicsHost> logger)
        {
            _logger = logger;
            _staticLogger = logger;
            _logger.LogDebug("[GraphicsHost] Constructor called");
            Array.Fill(_discReleasedAt, double.NegativeInfinity);
            _backgroundThread = new Thread(Run);
            _backgroundThread.Start();
            _logger.LogDebug("[GraphicsHost] Background thread started");
        }
        [StructLayout(LayoutKind.Sequential)]
        struct PushData
        {
            public Vector2 Resolution;         //  8
            public float   Time;               //  4
            public float   DistortionStrength; //  4
            public float   HueTolerance;       //  4
            public float   Falloff;            //  4
            public float   Param1;             //  4
            public float   Param2;             //  4
            public float   Param3;             //  4
            public float   Param4;             //  4  = 40 bytes
            // AudioLite v1 — append-only from here (version bumps add to the end; never
            // reorder, never repurpose). Well under the 128-byte guaranteed push budget.
            // Shaders may declare only a prefix of this block; audiolite.frag carries the
            // canonical GLSL mirror.
            public float   AudioEnergy;        //  4
            public float   AudioImpulse;       //  4
            public float   AudioTension;       //  4
            public float   AudioPhase;         //  4  bar phase, pre-advanced to display time
            public float   AudioHue;           //  4
            public float   AudioGrain;         //  4
            public float   AudioMotion;        //  4
            public float   AudioSpread;        //  4  = 72 bytes total
        }

        // Immutable holder + one volatile reference: the SetSampleMap idiom. Any thread
        // publishes; the render thread reads the latest exactly once per frame.
        private sealed class AudioLiteHolder
        {
            public readonly AudioLitePush Value;
            public AudioLiteHolder(in AudioLitePush value) { Value = value; }
        }

        private AudioLiteHolder _audioLite = new(AudioLitePush.Silence);

        /// <inheritdoc />
        public void SetAudio(in AudioLitePush audio) =>
            Volatile.Write(ref _audioLite, new AudioLiteHolder(audio));

        /// <summary>Fixed spectrum size the SSBO header and shaders assume.</summary>
        internal const int SpectrumBins = 1024;

        /// <summary>Injection bands appended after the bins, as all the positions across,
        /// then all the depths, then all the hues, then all the pulses, then the two bias
        /// values, the marble amount and the pad hold — must match INJ_BANDS in fluid.comp and
        /// InjectionHistory.BandCount (three places that must agree).</summary>
        internal const int InjectionSites = 5;

        /// <summary>One published payload: the bins, then the injection table.</summary>
        private const int SpectrumPayloadFloats = SpectrumBins + InjectionTableFloats;
        private const int InjectionTableFloats = InjectionSites * 4 + 5;

        /// <summary>
        /// Spectrum publication: the AudioLiteHolder idiom, but the 4 KB payload (bins
        /// followed by the injection sites, so the pair can never tear) lives in a
        /// 3-deep preallocated ring so publishing never allocates the array. A wrap-around
        /// tear would need two publishes (~21 ms) to land inside the render thread's
        /// microsecond memcpy of the previous holder — effectively impossible, and the
        /// consequence would be one visually blended hop.
        /// </summary>
        private sealed class SpectrumHolder
        {
            public readonly float[] Bins;
            public readonly int Version;
            public SpectrumHolder(float[] bins, int version) { Bins = bins; Version = version; }
        }

        private readonly float[][] _spectrumRing =
        {
            new float[SpectrumPayloadFloats], new float[SpectrumPayloadFloats], new float[SpectrumPayloadFloats],
        };
        private int _spectrumWriteIndex;
        private int _spectrumPublishVersion;
        private SpectrumHolder _spectrum = new(HomeSitesPayload(), 0);

        /// <summary>Silence with every injection site at home (plain frequency order
        /// along the top, a golden-angle hue per band) — what a shader sees before the first publish.</summary>
        private static float[] HomeSitesPayload()
        {
            var payload = new float[SpectrumPayloadFloats];
            FillHomeSites(payload.AsSpan(SpectrumBins));
            return payload;
        }

        private static void FillHomeSites(Span<float> sites)
        {
            for (int i = 0; i < InjectionSites; i++)
            {
                sites[i] = (i + 0.5f) / InjectionSites;
                sites[InjectionSites + i] = 0f;
                sites[InjectionSites * 2 + i] = i * 0.381966f % 1f;
                sites[InjectionSites * 3 + i] = 0f; // nothing injecting
            }
            sites[InjectionSites * 4] = 0f;         // no bias, in either form
            sites[InjectionSites * 4 + 1] = 0f;
            sites[InjectionSites * 4 + 2] = 1f;     // fully marbled
            sites[InjectionSites * 4 + 3] = 0f;     // no pads held
            sites[InjectionSites * 4 + 4] = 0f;     // nothing stirring
        }
        private float _dropSignal = 999f;

        /// <inheritdoc />
        public void SetDropSignal(float signedSeconds)
        {
            Volatile.Write(ref _dropSignal,
                float.IsFinite(signedSeconds) ? Math.Clamp(signedSeconds, -999f, 999f) : 999f);
        }

        /// <inheritdoc />
        public void SetAudioSpectrum(ReadOnlySpan<float> magnitudes, ReadOnlySpan<float> injectionTable)
        {
            var target = _spectrumRing[_spectrumWriteIndex];
            _spectrumWriteIndex = (_spectrumWriteIndex + 1) % _spectrumRing.Length;
            var count = Math.Min(magnitudes.Length, SpectrumBins);
            magnitudes.Slice(0, count).CopyTo(target);
            if (count < SpectrumBins)
            {
                Array.Clear(target, count, SpectrumBins - count);
            }
            var sites = target.AsSpan(SpectrumBins);
            if (injectionTable.Length == sites.Length)
            {
                injectionTable.CopyTo(sites);
            }
            else
            {
                FillHomeSites(sites);
            }
            Volatile.Write(ref _spectrum, new SpectrumHolder(target, ++_spectrumPublishVersion));
        }

        private Vk _vk;
        private Instance _instance;
        private SurfaceKHR _surface;
        private KhrSurface _khrSurface;
#if MACCATALYST
        private ExtMetalSurface _extMetalSurface;
#elif WINDOWS
        private KhrWin32Surface _khrWin32Surface;
#endif
        private PhysicalDevice _physicalDevice;
        private Device _device;
        private Queue _graphicsQueue;
        private KhrSwapchain _khrSwapchain;
        private SwapchainKHR _swapchain;
        private Silk.NET.Vulkan.Image[] _swapchainImages;
        private ImageView[] _swapchainImageViews;

        private CommandPool _commandPool;
        private CommandBuffer[] _commandBuffers;

        private RenderPass _renderPass;
        private RenderPass _offscreenRenderPass;
        private Framebuffer[] _framebuffers;

        private KhrSwapchain _swapchainExtension;
        private Pipeline _pipeline;
        private PipelineLayout _pipelineLayout;

        ulong[] _imageSerial;
        private Semaphore[] _imageAvailableSemaphores;
        private Semaphore[] _renderFinishedByImage; // one per swapchain image

        private Semaphore _gpuDoneTL; // produced-by-GPU
        private Semaphore _cpuDoneTL; // released-by-CPU

        // Global counters for each timeline
        private ulong _gpuCtr = 0;
        private ulong _cpuCtr = 0;

        // Per-slot bookkeeping (ring size = StagingRing.RingSize)
        private readonly ulong[] _slotReadyGpuValue = new ulong[StagingRing.RingSize]; // value the CPU should wait for
        private readonly ulong[] _slotNeedCpuRelease = new ulong[StagingRing.RingSize]; // last value that must be released before reuse

        ulong _timelineValue = 0;
        ulong[] _slotSignaledValue = new ulong[3];
        private ulong _cpuSignaled = 0;
        private long _pendingMemoryReads = 0;
        private Fence _submitFence;
        private readonly ManualResetEventSlim _readReadyEvent = new(initialState: true);

        private int _currentFrame = 0;
        private StagingRing _stagingRing;

        private ShaderModule _vertShaderModule;
        private ShaderModule _fragShaderModule;

        private uint _graphicsQueueIndex;
        private PhysicalDeviceMemoryProperties _memProps;
        private Extent2D _swapExtent;
        private DeviceMemory _offscreenMemory;
        private Silk.NET.Vulkan.Image _offscreenImage;
        private ImageView _offscreenView;
        private Framebuffer _offscreenFramebuffer;
        private ulong _imageSize;

        private bool _imageAcquired = false;
        private uint _acquiredImageIndex = 0;

        /// <summary>
        /// Which frame slot's image-available semaphore belongs to the image we are holding.
        /// <para>
        /// An acquire signals <c>_imageAvailableSemaphores[_currentFrame]</c>, and the submit that
        /// consumes the image has to wait on that same one. They are not the same slot whenever a
        /// frame acquires and a later frame presents — which is every time presentation is turned
        /// off with an image in hand: the window went away, frames keep rendering for the rig, and
        /// <c>_currentFrame</c> advances the whole time. Waiting on the current slot's semaphore
        /// then waits on something nothing will ever signal, the submit never retires, and the
        /// unbounded fence wait at the top of the next frame takes the render loop and the rig with
        /// it. Only meaningful while <see cref="_imageAcquired"/> is true, which stays the authority
        /// on whether an image is held.
        /// </para>
        /// </summary>
        private int _acquiredFrameIndex = -1;

        private Silk.NET.Vulkan.Buffer _buffer;

        private Stopwatch _stopwatch = new Stopwatch();

        private struct EffectiveColor
        {
            public System.Drawing.Color Color;
            public double AddedTime;
            public double RemovedTime; // double.NegativeInfinity while still active
        }

        private readonly List<EffectiveColor> _effectiveColors = new List<EffectiveColor>();
        // Snapshot of color state taken just before a color-count change, used for SSBO cross-fade
        private List<Vector4> _snapshotColors = new List<Vector4>();
        private double _transitionStart = double.NegativeInfinity;
        // This frame's color state as the injection table reads it (built by
        // UpdateColorState, which runs before the spectrum upload): the pads HELD, each
        // with its fade-in weight, and separately the colour most recently released with
        // its fade-out weight. The split is the point: the injection cycles only among the
        // held colours, so the colour just pressed is injected now rather than when the
        // per-bar cycle next comes round to it past the entries still fading out.
        // Per injection disc, the pad colour on it: its hue, and when it was pressed and
        // released (NegativeInfinity = never / still down), so the hue snaps in on the
        // press and fades out over PAD_INJECT_FADE_OUT_SECS after the release. Rebuilt
        // from PadColors every frame by UpdateColorState; read by WriteSpectrumData.
        private readonly float[] _discHue = new float[InjectionSites];
        private readonly double[] _discHeldAt = new double[InjectionSites];
        private readonly double[] _discReleasedAt = new double[InjectionSites];
        private readonly bool[] _discHeld = new bool[InjectionSites];
        private readonly System.Drawing.Color[] _discColor = new System.Drawing.Color[InjectionSites];
        private static readonly List<Vector4> NoColors = new List<Vector4>();
        // How long a pad's color takes to arrive after the press and to leave after the
        // release — and the cross-fade when the set of held colors changes — in the
        // region colours the kaleidoscope and the fluid's overlay read from the color
        // SSBO. Two seconds: the regions wash in and out rather than cut.
        private const double COLOR_FADE_DURATION_SECS = 2.0;
        // The fluid's injection hues and hue-spread flattening follow the pads on their own
        // fades, shaped to the FLOOD a press fires through the analysis (InjectionPulses:
        // a 0.64s attack, 1.76s hold, 2s release): in with the attack, so the dye of a
        // tap is the tap's colour from the first of it, and out over the whole envelope
        // after the release, so the flood a tap leaves behind stays in its colour to the
        // end rather than turning back to the palette half way through. (Before the
        // flood these matched the overlay's 2s so the pad's effects eased together; a
        // 0.1s rise then read as a snap against the overlay still washing in. The rise
        // is now as near a snap as a frame allows: a burst is wanted, not a ramp.)
        private const double PAD_INJECT_FADE_OUT_SECS = 4.5;

        // Per-frame SSBO resources (one per MAX_FRAMES_IN_FLIGHT slot)
        private DescriptorSetLayout _ssboSetLayout;
        private DescriptorPool      _ssboPool;
        // One descriptor set per (frame slot, ping-pong parity): [slot * 2 + parity].
        // Shaders without a compute prelude always use parity 0.
        private DescriptorSet[]     _frameSets    = new DescriptorSet[MAX_FRAMES_IN_FLIGHT * 2];
        private Buffer[]            _spectrumBuffers  = new Buffer[MAX_FRAMES_IN_FLIGHT];
        private DeviceMemory[]      _spectrumMemories = new DeviceMemory[MAX_FRAMES_IN_FLIGHT];
        private unsafe void*[]      _spectrumMapped   = new void*[MAX_FRAMES_IN_FLIGHT];
        private const uint SpectrumBufferBytes = 16 + SpectrumPayloadFloats * sizeof(float); // header + bins + injection table

        // The generic compute-prelude hook: a shader that ships a {name}.comp.spv gets a
        // simulation pass created at startup and recorded before the render pass while it
        // is the active shader. Currently only the fluid effect uses it.
        private FluidSimulationPass? _computePrelude;
        private string? _computePreludeShader;
        private Buffer[]            _ssboBuffers  = new Buffer[MAX_FRAMES_IN_FLIGHT];
        private DeviceMemory[]      _ssboMemories = new DeviceMemory[MAX_FRAMES_IN_FLIGHT];
        private unsafe void*[]      _ssboMapped   = new void*[MAX_FRAMES_IN_FLIGHT];

        const int MAX_FRAMES_IN_FLIGHT = 3;

        public object _lock = new object();
        public byte[] Bytes = null;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public System.Drawing.Color[] Colors { get; set; } = Array.Empty<System.Drawing.Color>();
        public PadColor[] PadColors { get; set; } = Array.Empty<PadColor>();
        public bool ColorRegions { get; set; } = true;
        public float DistortionStrength { get; set; } = 0.12f;
        public float ShaderParam1 { get; set; } = 2.5f;   // spiral twist for kaleidoscope / blade count for clown
        public float ShaderParam2 { get; set; } = 0.0f;   // animation time offset
        public float ShaderParam3 { get; set; } = 3.0f;   // radial color ring density (k base)
        public float ShaderParam4 { get; set; } = 10.0f;  // flow pattern frequency
        public string CurrentShader => _shader;

        private bool PRESENT = false; // set false to run headless (no window present)
        private bool PRESENT_DCOMP = false; // DirectComposition mode (Vulkan/D3D11 interop)
        private Extent2D _offscreenExtent;
        private CommandBuffer[] _cmds;

#if WINDOWS
        // DirectComposition interop fields
        private VulkanExternalMemory? _vulkanExternalMemory;
        private IDirectCompositionPresenter? _dcompPresenter;
        private uint _dcompWidth;
        private uint _dcompHeight;
        private ulong _dcompMutexAcquireKey = 0;
        private ulong _dcompMutexReleaseKey = 1;
        private volatile bool _presentInProgress = false;
#endif

        private volatile bool _running;
        private Thread _renderThread;

        private unsafe void* _mapped;

        private volatile bool _recreatingSwapchain = false;
        private volatile bool _isClosing = false;
        private volatile bool _tearingDownWSI = false;
        private volatile bool _resizePending = false;

        /// <summary>
        /// Set when the GPU reports the device gone. Terminal: nothing below this class survives it,
        /// so the render loop stops rather than resubmitting work that can only fail again. Getting
        /// the picture back needs the whole Vulkan device rebuilding, which is a restart today.
        /// </summary>
        private volatile bool _deviceLost = false;

        /// <summary>Tracks the paused/resumed transition so it is logged once, not per frame.</summary>
        private bool _presentationSuspended;

        /// <summary>
        /// A call into the presentation stack taking longer than this is not normal work: it is the
        /// compositor refusing to hand over a drawable, which it does while the window is minimised
        /// or hidden. Metal will sit on the request for up to a second.
        /// </summary>
        private static readonly TimeSpan PresentStallThreshold = TimeSpan.FromMilliseconds(100);

        /// <summary>How long to leave presentation alone after a stall before trying again.</summary>
        private static readonly TimeSpan PresentRetryInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// How often the loop renders a frame.
        /// <para>
        /// Presentation used to set this rate by accident. With FIFO, an acquire that found no free
        /// swapchain image returned early and the frame did no work at all, so the loop advanced only
        /// as fast as the compositor released images — the display's refresh, near enough. Once a
        /// frame that cannot be presented had to render anyway (the rig's pixels are in that same
        /// submission and must not stop when the window does), nothing was left holding the loop
        /// back and it free-ran at whatever the GPU would do: hundreds of full renders and 6.5MB
        /// staging copies a second, feeding a rig that samples 46 of them.
        /// </para>
        /// <para>
        /// So it is paced explicitly instead, and no longer depends on a swapchain existing. It is
        /// set to the rate the rig actually consumes — the same 21739µs <c>RenderService</c> paces
        /// its own loop to — because a frame rendered between two samples is a frame nobody reads.
        /// Matching exactly rather than leaving headroom means jitter on either loop occasionally
        /// costs a sample its fresh frame, and the sampler holds the previous one for a tick; that
        /// is invisible on LEDs and cheaper than rendering spares against it. Keep the two in step
        /// if either changes.
        /// </para>
        /// </summary>
        private static readonly TimeSpan TargetFrameInterval = TimeSpan.FromMicroseconds(21739);


        /// <summary>
        /// Presentation is skipped entirely until this time. Backing off after a stall is what keeps
        /// a minimised window from costing the render loop a blocking call every frame — the frames
        /// still render and the rig still gets its pixels, they just are not shown to nobody.
        /// </summary>
        private TimeSpan _presentRetryAfter;

        private readonly OutputDeviceHealth _presentStallHealth = new OutputDeviceHealth();

        /// <summary>
        /// False while the preview window is hidden. See <see cref="SetPresentationEnabled"/>: a
        /// frame that carries swapchain work waits on a drawable a minimised window will not give,
        /// and the rig's pixels are in the same submission.
        /// </summary>
        private volatile bool _presentationEnabled = true;

        public void SetPresentationEnabled(bool enabled)
        {
            if (_presentationEnabled == enabled)
            {
                return;
            }

            _presentationEnabled = enabled;
            _logger.LogDebug("[GraphicsHost] Presentation {State} (preview window {Visibility}).",
                enabled ? "enabled" : "disabled", enabled ? "visible" : "hidden");
        }

        /// <summary>
        /// Throttles reporting from the render loop, which runs thousands of times a second: a
        /// failure that repeats every frame is logged once, then at most every 30s. Without it a
        /// persistent failure writes tens of thousands of lines a minute, and the disk and the
        /// logging pipeline take the process down with them — which is exactly what a lost device
        /// did before this existed.
        /// </summary>
        private readonly OutputDeviceHealth _renderHealth = new OutputDeviceHealth();

        /// <summary>
        /// Serializes swapchain teardown/creation against itself and against cleanup. Two threads
        /// recreating at once is how the device gets lost.
        /// </summary>
        private readonly object _swapchainLock = new object();
        private bool _previewWindowOpen = false;
        private bool _loggedPresentStart = false;
        private bool _loggedFirstPresent = false;
        private bool _loggedFirstBlit = false;
        private bool _loggedDcompBlitSize = false;
        private CancellationTokenSource? _resizeDebounceToken;
        private uint _pendingResizeWidth;
        private uint _pendingResizeHeight;

        public void Run()
        {
            _logger.LogDebug("[GraphicsHost] Run() started");
            try
            {
                //System.Threading.Thread.Sleep(20000);
                _logger.LogDebug("[GraphicsHost] Calling RunHeadless()");
                RunHeadless();
                _logger.LogDebug("[GraphicsHost] RunHeadless() completed");
                // Auto-open preview window
                _logger.LogDebug("[GraphicsHost] Calling ShowPreviewWindow()");
                ShowPreviewWindow();
                _logger.LogDebug("[GraphicsHost] ShowPreviewWindow() dispatched");
            }
            catch (Exception e)
            {
                _logger.LogError($"[GraphicsHost] Exception in Run(): {e.Message}");
                _logger.LogError($"[GraphicsHost] Stack trace: {e.StackTrace}");
            }
        }

        /// <summary>
        /// Starts the offscreen render loop. The default extent is the coordinate space every pixel
        /// map is authored in, so it has to stay in step with <c>PixelMap.DefaultImageWidth</c> and
        /// <c>DefaultImageHeight</c> (ShowControl.PixelMapping, which this project does not
        /// reference). The shaders normalize by <c>u_resolution</c>, so changing it changes
        /// resolution rather than the picture — as long as the aspect ratio is kept.
        /// </summary>
        public void RunHeadless(int width = 1350, int height = 1200)
        {
            PRESENT = false; // make sure your OnRender uses the no-present path
            _offscreenExtent = new Extent2D((uint)width, (uint)height);
            Width = width;
            Height = height;
            _swapExtent = _offscreenExtent; // reuse same var if your code expects it
            _imageSize = (ulong)width * (ulong)height * 4;

            OnLoadHeadless();  // custom init that skips window/swapchain
            _running = true;
            _renderThread = new Thread(RenderLoop) { IsBackground = true };
            _renderThread.Start();
        }

        private void RenderLoop()
        {
            _stopwatch.Start();
            while (_running && !_deviceLost)
            {
                var frameStarted = _stopwatch.Elapsed;
                OnRender(0);

                // Sleep the rest of the frame. What was here was Thread.Sleep(0), which despite its
                // comment throttles nothing: it yields to another runnable thread if one is waiting
                // and returns immediately otherwise, so the loop simply ran flat out on a core.
                var remaining = TargetFrameInterval - (_stopwatch.Elapsed - frameStarted);
                Thread.Sleep(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            }

            if (_deviceLost)
            {
                _logger.LogError(
                    "[GraphicsHost] Render loop stopped: the GPU device was lost. The preview and the sampled pixels are frozen on the last good frame; restarting the app is the only way back.");
            }
        }

        /// <summary>
        /// Reports a failure from inside the render loop at most once per outage, plus a reminder
        /// every 30s. The loop runs thousands of times a second, so anything that logs per attempt
        /// here is not a diagnostic — it is a denial of service against the app itself.
        /// </summary>
        private void ReportRenderFailure(string what, Exception? exception = null)
        {
            var transition = _renderHealth.RecordFailure(_stopwatch.Elapsed);
            switch (transition.Action)
            {
                case OutputDeviceLogAction.WentDown:
                    _logger.LogError(exception, "[GraphicsHost] {What}; further occurrences suppressed until it recovers.", what);
                    break;
                case OutputDeviceLogAction.StillDown:
                    _logger.LogError("[GraphicsHost] {What} — still failing after {Attempts} attempts over {Seconds:F0}s.",
                        what, transition.FailedAttempts, transition.DownFor.TotalSeconds);
                    break;
            }
        }

        private void ReportRenderSuccess()
        {
            if (_renderHealth.IsDown &&
                _renderHealth.RecordSuccess(_stopwatch.Elapsed).Action == OutputDeviceLogAction.Recovered)
            {
                _logger.LogInformation("[GraphicsHost] Rendering resumed.");
            }
        }

        /// <summary>
        /// Logs the preview going quiet and coming back, once per transition rather than per frame.
        /// The rig keeps being fed either way — this is about being able to tell afterwards what the
        /// window did when someone says "it does something on minimise".
        /// </summary>
        private void NotePresentationSuspended(string reason)
        {
            if (_presentationSuspended)
            {
                return;
            }

            _presentationSuspended = true;
            _logger.LogDebug("[GraphicsHost] Presentation paused: {Reason}. Offscreen rendering and pixel sampling continue.", reason);
        }

        /// <summary>
        /// Notes a blocking call into the presentation stack and backs presentation off for a
        /// second. Reported once per outage plus a reminder, never per frame.
        /// </summary>
        private void NotePresentStall(string where, TimeSpan took)
        {
            _presentRetryAfter = _stopwatch.Elapsed + PresentRetryInterval;

            var transition = _presentStallHealth.RecordFailure(_stopwatch.Elapsed);
            switch (transition.Action)
            {
                case OutputDeviceLogAction.WentDown:
                    _logger.LogDebug(
                        "[GraphicsHost] {Where} blocked for {Milliseconds:F0}ms — the window is minimised or hidden. Presentation backs off to {Retry:F0}s while the rig keeps rendering; further stalls suppressed.",
                        where, took.TotalMilliseconds, PresentRetryInterval.TotalSeconds);
                    break;
                case OutputDeviceLogAction.StillDown:
                    _logger.LogDebug(
                        "[GraphicsHost] {Where} still stalling: {Attempts} times over {Seconds:F0}s.",
                        where, transition.FailedAttempts, transition.DownFor.TotalSeconds);
                    break;
            }
        }

        private void NotePresentationResumed()
        {
            if (_presentStallHealth.IsDown &&
                _presentStallHealth.RecordSuccess(_stopwatch.Elapsed).Action == OutputDeviceLogAction.Recovered)
            {
                _logger.LogDebug("[GraphicsHost] Presentation stalls cleared.");
            }

            if (!_presentationSuspended)
            {
                return;
            }

            _presentationSuspended = false;
            _logger.LogDebug("[GraphicsHost] Presentation resumed.");
        }

        /// <summary>
        /// Latches a lost device. Everything built on it — pipelines, swapchain, the staging ring the
        /// LED sampling reads — is invalid from here, so the loop is stopped instead of being left to
        /// resubmit work that cannot succeed.
        /// </summary>
        private void ReportDeviceLost(string where)
        {
            if (_deviceLost)
            {
                return;
            }

            _deviceLost = true;
            _logger.LogError("[GraphicsHost] {Where} reported VK_ERROR_DEVICE_LOST; stopping the render loop.", where);
        }

        public void StopHeadless()
        {
            _running = false;
            _renderThread?.Join();

        }

        // Shared methods used by both ARM64 and non-ARM64 presentation code
        private unsafe uint FindPresentQueueFamily(PhysicalDevice pd, SurfaceKHR surface)
        {
            uint count = 0;
            _vk.GetPhysicalDeviceQueueFamilyProperties(pd, ref count, null);
            var props = new QueueFamilyProperties[count];
            fixed (QueueFamilyProperties* p = props)
                _vk.GetPhysicalDeviceQueueFamilyProperties(pd, ref count, p);

            for (uint i = 0; i < count; i++)
            {
                Bool32 supports;
                _khrSurface.GetPhysicalDeviceSurfaceSupport(pd, i, surface, out supports);
                if (supports) return i;
            }
            throw new Exception("No PRESENT-capable queue family.");
        }

        private unsafe void DestroyPerImageSyncObjects()
        {
            if (_renderFinishedByImage != null)
            {
                foreach (var s in _renderFinishedByImage)
                    if (s.Handle != 0) _vk.DestroySemaphore(_device, s, null);
                _renderFinishedByImage = null;
            }
        }

        private unsafe void CreatePerImageSyncObjects(uint imageCount)
        {
            DestroyPerImageSyncObjects(); // safe if null
            _renderFinishedByImage = new Semaphore[imageCount];

            var semInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
            for (int i = 0; i < _renderFinishedByImage.Length; i++)
                _vk.CreateSemaphore(_device, semInfo, null, out _renderFinishedByImage[i]);
        }

        private SurfaceTransformFlagsKHR PickPreTransform(in SurfaceCapabilitiesKHR caps)
        {
            var identity = SurfaceTransformFlagsKHR.SurfaceTransformIdentityBitKhr;
            if ((caps.SupportedTransforms & identity) != 0)
                return identity;

            if (caps.CurrentTransform != 0)
                return caps.CurrentTransform;

            // Fallback: pick the lowest set bit in SupportedTransforms
            foreach (SurfaceTransformFlagsKHR bit in Enum.GetValues(typeof(SurfaceTransformFlagsKHR)))
            {
                if (bit != 0 && (caps.SupportedTransforms & bit) != 0)
                    return bit;
            }

            // Last resort (shouldn't happen on a valid surface):
            return identity;
        }

#if MACCATALYST
        private nint _metalLayerPtr;
        private uint _metalSurfaceWidth;
        private uint _metalSurfaceHeight;

        /// <summary>
        /// Creates a Vulkan surface from a CAMetalLayer pointer and sets up the swapchain for presentation.
        /// Called when the MetalSurfaceView's layer is ready.
        /// </summary>
        public unsafe void EnableMetalPresentation(nint metalLayerPtr, uint width, uint height)
        {
            if (_surface.Handle != 0)
            {
                // Already presenting to this layer. There is nothing to bring up to date: the
                // drawable is pinned, so the size cannot have changed under us.
                return;
            }

            _metalLayerPtr = metalLayerPtr;
            _metalSurfaceWidth = width;
            _metalSurfaceHeight = height;

            // Create Metal surface using VK_EXT_metal_surface
            var createInfo = new MetalSurfaceCreateInfoEXT
            {
                SType = StructureType.MetalSurfaceCreateInfoExt,
                PLayer = (nint*)metalLayerPtr
            };

            var result = _extMetalSurface.CreateMetalSurface(_instance, &createInfo, null, out _surface);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create Metal surface: {result}");
            }

            // Initialize swapchain extension
            _swapchainExtension = new KhrSwapchain(_vk.Context);

            // Create swapchain
            CreateSwapchainForMetalSurface(width, height);

            // Enable presentation path in render loop
            PRESENT = true;
        }

        /// <summary>
        /// Nothing to do: the Metal drawable is pinned to the offscreen image's size, so a window
        /// resize does not invalidate the swapchain and there is nothing to rebuild.
        /// <para>
        /// This used to debounce and then recreate the swapchain on every resize, which is what a
        /// renderer drawing at the window's resolution has to do. This one does not draw at the
        /// window's resolution — it blits a fixed image — so matching the swapchain to the window
        /// only moved the final upscale out of the compositor and into the blit, at the price of a
        /// heavyweight GPU rebuild on a path driven by a mouse drag. Overlapping rebuilds from a
        /// fast resize are what took the GPU device out and hung the app.
        /// </para>
        /// </summary>
        public void HandleMetalSurfaceResize(uint width, uint height)
        {
        }

        /// <summary>
        /// Creates or recreates the swapchain for the Metal surface.
        /// </summary>
        private unsafe void CreateSwapchainForMetalSurface(uint width, uint height)
        {
            // Query surface capabilities
            _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var caps);

            // Clamp extent to surface capabilities
            var extent = new Extent2D(
                Math.Clamp(width, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                Math.Clamp(height, caps.MinImageExtent.Height, caps.MaxImageExtent.Height)
            );

            // Query supported formats
            uint fmtCount = 0;
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, null);
            var fmts = new SurfaceFormatKHR[fmtCount];
            fixed (SurfaceFormatKHR* p = fmts)
                _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, p);

            // On Metal, prefer B8G8R8A8_SRGB (native format), then fall back to R8G8B8A8_SRGB
            var pickFmt = fmts[0];
            bool foundPreferred = false;
            foreach (var f in fmts)
            {
                // First choice: B8G8R8A8 (Metal native)
                if (f.Format == Format.B8G8R8A8Srgb && f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                {
                    pickFmt = f;
                    foundPreferred = true;
                    break;
                }
                // Second choice: R8G8B8A8
                if (!foundPreferred && f.Format == Format.R8G8B8A8Srgb && f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                {
                    pickFmt = f;
                }
            }

            // Image count
            uint imageCount = Math.Max(2, caps.MinImageCount);
            if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
                imageCount = caps.MaxImageCount;

            var preTransform = PickPreTransform(caps);

            var sci = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = pickFmt.Format,
                ImageColorSpace = pickFmt.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ImageUsageTransferDstBit, // We blit into it
                ImageSharingMode = SharingMode.Exclusive,
                PreTransform = preTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                PresentMode = PresentModeKHR.PresentModeFifoKhr, // FIFO is always supported
                Clipped = true,
                OldSwapchain = default
            };

            var result = _swapchainExtension.CreateSwapchain(_device, ref sci, null, out _swapchain);
            if (result != Result.Success)
                throw new Exception($"vkCreateSwapchainKHR failed: {result}");

            // Fetch swapchain images
            uint count = 0;
            _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, null);
            _swapchainImages = new Image[count];
            _imageSerial = new ulong[count];
            fixed (Image* pImgs = _swapchainImages)
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, pImgs);

            CreatePerImageSyncObjects(count);
            _swapExtent = extent;
        }

        /// <summary>
        /// Rebuilds the swapchain at the pinned drawable size.
        /// <para>
        /// With the drawable pinned this should never be needed — the swapchain's extent cannot stop
        /// matching a layer whose drawable size never changes. It exists for the case where the
        /// compositor invalidates the swapchain anyway, a display reconfiguration being the likely
        /// one: without it the acquire would report out-of-date and the preview would skip frames
        /// for the rest of the session.
        /// </para>
        /// </summary>
        private void RebuildSwapchainAtPinnedSize()
        {
            if (_deviceLost || _isClosing || _tearingDownWSI || _surface.Handle == 0)
            {
                return;
            }

            lock (_swapchainLock)
            {
                if (_deviceLost || _isClosing || _tearingDownWSI || _surface.Handle == 0)
                {
                    return;
                }

                // Through the throttle: if this ever does start repeating, it must not become the
                // log storm the old resize path was.
                ReportRenderFailure("Swapchain reported out of date; rebuilding at the pinned drawable size");
                RecreateSwapchainForSize(_metalSurfaceWidth, _metalSurfaceHeight);
            }
        }

        /// <summary>
        /// Rebuilds the swapchain at a new size. Callers must hold <c>_swapchainLock</c>: two of
        /// these running at once destroy each other's objects, and MoltenVK answers that by losing
        /// the device.
        /// </summary>
        private unsafe void RecreateSwapchainForSize(uint width, uint height)
        {
            // Stop OnRender from running during swapchain recreation
            _recreatingSwapchain = true;
            _imageAcquired = false;

            // The whole device, not just the graphics queue: the staging ring the LED sampling reads
            // is filled by transfers on this device too, and the images about to be destroyed may
            // still be referenced by them.
            _vk.DeviceWaitIdle(_device);

            try
            {
            // Query surface capabilities
            _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var caps);

            _logger.LogDebug($"[GraphicsHost] RecreateSwapchain: requested={width}x{height}, caps.current={caps.CurrentExtent.Width}x{caps.CurrentExtent.Height}, caps.min={caps.MinImageExtent.Width}x{caps.MinImageExtent.Height}, caps.max={caps.MaxImageExtent.Width}x{caps.MaxImageExtent.Height}");

            // On Metal, use the requested size directly since CAMetalLayer.drawableSize is already set
            var extent = new Extent2D(width, height);

            // Query formats again
            uint fmtCount = 0;
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, null);
            var fmts = new SurfaceFormatKHR[fmtCount];
            fixed (SurfaceFormatKHR* p = fmts)
                _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, p);

            // On Metal, prefer B8G8R8A8_SRGB (native format), then fall back to R8G8B8A8_SRGB
            var pickFmt = fmts[0];
            bool foundPreferred = false;
            foreach (var f in fmts)
            {
                // First choice: B8G8R8A8 (Metal native)
                if (f.Format == Format.B8G8R8A8Srgb && f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                {
                    pickFmt = f;
                    foundPreferred = true;
                    break;
                }
                // Second choice: R8G8B8A8
                if (!foundPreferred && f.Format == Format.R8G8B8A8Srgb && f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                {
                    pickFmt = f;
                }
            }

            uint imageCount = Math.Max(2, caps.MinImageCount);
            if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
                imageCount = caps.MaxImageCount;

            var preTransform = PickPreTransform(caps);

            var sci = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = pickFmt.Format,
                ImageColorSpace = pickFmt.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ImageUsageTransferDstBit,
                ImageSharingMode = SharingMode.Exclusive,
                PreTransform = preTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                PresentMode = PresentModeKHR.PresentModeFifoKhr,
                Clipped = true,
                OldSwapchain = _swapchain // Pass old swapchain for safe recreation
            };

            var result = _swapchainExtension.CreateSwapchain(_device, ref sci, null, out var newSwapchain);
            if (result != Result.Success)
                throw new Exception($"vkCreateSwapchainKHR failed: {result}");

            // Destroy old swapchain after creating new one
            if (_swapchain.Handle != 0)
            {
                DestroyPerImageSyncObjects();
                _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
            }
            _swapchain = newSwapchain;

            // Fetch new swapchain images
            uint count = 0;
            _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, null);
            _swapchainImages = new Image[count];
            _imageSerial = new ulong[count];
            fixed (Image* pImgs = _swapchainImages)
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, pImgs);

            CreatePerImageSyncObjects(count);
            _swapExtent = extent;
            _imageAcquired = false;
            }
            finally
            {
                // Resume OnRender
                _recreatingSwapchain = false;
            }
        }

        /// <summary>
        /// Cleans up Metal presentation resources when the window is closing.
        /// </summary>
        public unsafe void CleanupMetalPresentation()
        {
            _logger.LogDebug("[GraphicsHost] Tearing down Metal presentation (surface and swapchain destroyed).");
            PRESENT = false;
            _imageAcquired = false;
            _tearingDownWSI = true;

            // Under the same lock a rebuild takes: closing the window while one is in flight would
            // otherwise free the swapchain from under it.
            lock (_swapchainLock)
            {
                _vk.DeviceWaitIdle(_device);

                DestroyPerImageSyncObjects();

                if (_swapchain.Handle != 0)
                {
                    _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
                    _swapchain = default;
                }

                if (_surface.Handle != 0)
                {
                    _khrSurface.DestroySurface(_instance, _surface, null);
                    _surface = default;
                }

                _swapchainImages = null;
                _metalLayerPtr = 0;
            }

            _tearingDownWSI = false;
        }

        /// <summary>
        /// Event raised when the graphics host wants a preview window to be opened.
        /// The MAUI app should handle this event and create the window.
        /// </summary>
        public event EventHandler<PreviewWindowRequestedEventArgs>? PreviewWindowRequested;

        /// <summary>
        /// Requests the MAUI app to open a preview window.
        /// The app should handle the PreviewWindowRequested event.
        /// </summary>
        public void ShowPreviewWindow()
        {
            if (_previewWindowOpen)
            {
                return;
            }

            _logger.LogDebug("[GraphicsHost] Requesting preview window from MAUI app...");
            _previewWindowOpen = true;

            // Wait for event handler to be subscribed (race condition with MainPage.Initialize)
            Task.Run(async () =>
            {
                // Wait up to 5 seconds for a handler to be subscribed
                for (int i = 0; i < 50 && PreviewWindowRequested == null; i++)
                {
                    await Task.Delay(100);
                }

                if (PreviewWindowRequested == null)
                {
                    _logger.LogWarning("[GraphicsHost] No handler subscribed to PreviewWindowRequested after 5 seconds");
                    _previewWindowOpen = false;
                    return;
                }

                // Raise event on main thread
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    PreviewWindowRequested?.Invoke(this, new PreviewWindowRequestedEventArgs(
                        _offscreenExtent.Width,
                        _offscreenExtent.Height
                    ));
                });
            });
        }

        /// <summary>
        /// Called by the MAUI app when the preview window is closed.
        /// </summary>
        public void OnPreviewWindowClosed()
        {
            _logger.LogDebug("[GraphicsHost] Preview window closed");
            CleanupMetalPresentation();
            _previewWindowOpen = false;
        }

        // Win32 stubs for Mac Catalyst (not supported)
        public void EnableWin32Presentation(nint hwnd, uint width, uint height)
        {
            // Not supported on this platform
        }

        public void HandleWin32SurfaceResize(uint width, uint height)
        {
            // Not supported on this platform
        }

        public void EnableDirectCompositionPresentation(IDirectCompositionPresenter presenter, uint width, uint height)
        {
            // Not supported on this platform
        }

        public void HandleDirectCompositionResize(uint width, uint height)
        {
            // Not supported on this platform
        }

    /// <summary>
    /// UIView subclass with CAMetalLayer as its backing layer.
    /// </summary>
    private class MetalBackedUIView : UIView
    {
        public event EventHandler<nint>? LayerReady;
        public event EventHandler<CGSize>? SizeChanged;

        private bool _layerReported = false;

        public MetalBackedUIView() : base()
        {
            ContentScaleFactor = UIScreen.MainScreen.Scale;
            BackgroundColor = UIColor.Black;
        }

        [Export("layerClass")]
        public static Class LayerClass()
        {
            return new Class(typeof(CAMetalLayer));
        }

        public CAMetalLayer MetalLayer => (CAMetalLayer)Layer;

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();

            var newSize = new CGSize(
                Bounds.Width * ContentScaleFactor,
                Bounds.Height * ContentScaleFactor
            );

            if (newSize.Width > 0 && newSize.Height > 0)
            {
                MetalLayer.DrawableSize = newSize;
                SizeChanged?.Invoke(this, newSize);

                if (!_layerReported)
                {
                    _layerReported = true;
                    LayerReady?.Invoke(this, MetalLayer.Handle);
                }
            }
        }

        public override void MovedToWindow()
        {
            base.MovedToWindow();

            if (Window != null && !_layerReported && Bounds.Width > 0 && Bounds.Height > 0)
            {
                _layerReported = true;
                MetalLayer.DrawableSize = new CGSize(
                    Bounds.Width * ContentScaleFactor,
                    Bounds.Height * ContentScaleFactor
                );
                LayerReady?.Invoke(this, MetalLayer.Handle);
            }
        }
    }
#else
        // Stub implementations for non-MACCATALYST builds
        public event EventHandler<PreviewWindowRequestedEventArgs>? PreviewWindowRequested;

        /// <summary>
        /// Requests the MAUI app to open a preview window.
        /// The app should handle the PreviewWindowRequested event.
        /// </summary>
        public void ShowPreviewWindow()
        {
            if (_previewWindowOpen)
            {
                return;
            }

            _logger.LogDebug("[GraphicsHost] Requesting preview window from MAUI app...");
            _previewWindowOpen = true;

            // Wait for event handler to be subscribed (race condition with MainPage.Initialize)
            Task.Run(async () =>
            {
                // Wait up to 5 seconds for a handler to be subscribed
                for (int i = 0; i < 100 && PreviewWindowRequested == null; i++)
                {
                    await Task.Delay(100);
                }

                if (PreviewWindowRequested == null)
                {
                    _logger.LogWarning("[GraphicsHost] No handler subscribed to PreviewWindowRequested after 10 seconds");
                    _previewWindowOpen = false;
                    return;
                }

                // Raise event on main thread
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    PreviewWindowRequested?.Invoke(this, new PreviewWindowRequestedEventArgs(
                        _offscreenExtent.Width,
                        _offscreenExtent.Height
                    ));
                });
            });
        }

        public void EnableMetalPresentation(nint metalLayerPtr, uint width, uint height)
        {
            // Not supported on this platform
        }

        public void HandleMetalSurfaceResize(uint width, uint height)
        {
            // Not supported on this platform
        }

#if WINDOWS
        public unsafe void OnPreviewWindowClosed()
        {
            PRESENT = false;
            _imageAcquired = false;
            _tearingDownWSI = true;

            _vk.QueueWaitIdle(_graphicsQueue);

            DestroyPerImageSyncObjects();
            if (_swapchain.Handle != 0)
            {
                _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
                _swapchain = default;
            }
            if (_surface.Handle != 0)
            {
                _khrSurface.DestroySurface(_instance, _surface, null);
                _surface = default;
            }

            _swapchainImages = null;
            _acquiredImageIndex = 0;
            _tearingDownWSI = false;
            _previewWindowOpen = false;
        }

        private nint _win32Hwnd;
        private uint _win32SurfaceWidth;
        private uint _win32SurfaceHeight;

        /// <summary>
        /// Creates a Vulkan surface from a Win32 HWND and sets up the swapchain for presentation.
        /// </summary>
        public unsafe void EnableWin32Presentation(nint hwnd, uint width, uint height)
        {
            if (_surface.Handle != 0)
            {
                // Surface already exists, just resize
                HandleWin32SurfaceResize(width, height);
                return;
            }

            _logger.LogDebug($"[GraphicsHost] EnableWin32Presentation: hwnd=0x{hwnd:X}, size={width}x{height}");

            _win32Hwnd = hwnd;
            _win32SurfaceWidth = width;
            _win32SurfaceHeight = height;

            // Create Win32 surface
            _khrSurface = new KhrSurface(_vk.Context);

            var createInfo = new Win32SurfaceCreateInfoKHR
            {
                SType = StructureType.Win32SurfaceCreateInfoKhr,
                Hinstance = System.Diagnostics.Process.GetCurrentProcess().Handle,
                Hwnd = hwnd
            };

            var result = _khrWin32Surface.CreateWin32Surface(_instance, &createInfo, null, out _surface);
            if (result != Result.Success)
                throw new Exception($"vkCreateWin32SurfaceKHR failed: {result}");

            _logger.LogDebug($"[GraphicsHost] Win32 surface created: {_surface.Handle}");

            // Create swapchain
            _swapchainExtension = new KhrSwapchain(_vk.Context);
            CreateSwapchainForWin32Surface(width, height);

            _logger.LogDebug($"[GraphicsHost] Swapchain created: handle={_swapchain.Handle}, images={_swapchainImages?.Length ?? -1}, extent={_swapExtent.Width}x{_swapExtent.Height}");

            // Enable presentation
            PRESENT = true;
            _previewWindowOpen = true;
            _logger.LogDebug($"[GraphicsHost] Presentation enabled: PRESENT={PRESENT}");
        }

        private unsafe void CreateSwapchainForWin32Surface(uint width, uint height)
        {
            // Query surface capabilities
            _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var caps);

            var extent = new Extent2D(
                Math.Clamp(width, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                Math.Clamp(height, caps.MinImageExtent.Height, caps.MaxImageExtent.Height)
            );

            // Query formats
            uint fmtCount = 0;
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, null);
            var fmts = new SurfaceFormatKHR[fmtCount];
            fixed (SurfaceFormatKHR* p = fmts)
                _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, p);

            // Prefer B8G8R8A8 or R8G8B8A8 SRGB
            var pickFmt = fmts[0];
            foreach (var f in fmts)
            {
                if ((f.Format == Format.B8G8R8A8Srgb || f.Format == Format.R8G8B8A8Srgb) &&
                    f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                {
                    pickFmt = f;
                    break;
                }
            }

            uint imageCount = Math.Max(2, caps.MinImageCount);
            if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
                imageCount = caps.MaxImageCount;

            var preTransform = PickPreTransform(caps);

            var sci = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = pickFmt.Format,
                ImageColorSpace = pickFmt.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ImageUsageTransferDstBit,
                ImageSharingMode = SharingMode.Exclusive,
                PreTransform = preTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                PresentMode = PresentModeKHR.PresentModeFifoKhr,
                Clipped = true,
                OldSwapchain = default
            };

            var result = _swapchainExtension.CreateSwapchain(_device, ref sci, null, out _swapchain);
            if (result != Result.Success)
                throw new Exception($"vkCreateSwapchainKHR failed: {result}");

            // Fetch swapchain images
            uint count = 0;
            _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, null);
            _swapchainImages = new Image[count];
            _imageSerial = new ulong[count];
            fixed (Image* pImgs = _swapchainImages)
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, pImgs);

            CreatePerImageSyncObjects(count);
            _swapExtent = extent;
        }

        /// <summary>
        /// Handles resize of the Win32 surface by recreating the swapchain.
        /// </summary>
        public void HandleWin32SurfaceResize(uint width, uint height)
        {
            _logger.LogDebug($"[GraphicsHost] HandleWin32SurfaceResize: {width}x{height}, surface={_surface.Handle}");
            if (width == 0 || height == 0) return;
            if (_surface.Handle == 0) return;

            _win32SurfaceWidth = width;
            _win32SurfaceHeight = height;
            _pendingResizeWidth = width;
            _pendingResizeHeight = height;
            _resizePending = true;
            _logger.LogDebug($"[GraphicsHost] Set _resizePending = true");

            // Cancel any pending resize
            _resizeDebounceToken?.Cancel();
            _resizeDebounceToken = new CancellationTokenSource();
            var token = _resizeDebounceToken.Token;

            // Debounce resize
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100, token);
                    if (!token.IsCancellationRequested)
                    {
                        _logger.LogDebug($"[GraphicsHost] Debounce complete, recreating swapchain");
                        RecreateWin32Swapchain(_pendingResizeWidth, _pendingResizeHeight);
                        _resizePending = false;
                        _logger.LogDebug($"[GraphicsHost] Set _resizePending = false");
                    }
                }
                catch (TaskCanceledException) { }
            }, token);
        }

        private unsafe void RecreateWin32Swapchain(uint width, uint height)
        {
            _recreatingSwapchain = true;
            _imageAcquired = false;
            _vk.QueueWaitIdle(_graphicsQueue);

            try
            {
                // Query surface capabilities
                _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var caps);

                var extent = new Extent2D(
                    Math.Clamp(width, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                    Math.Clamp(height, caps.MinImageExtent.Height, caps.MaxImageExtent.Height)
                );

                // Query formats
                uint fmtCount = 0;
                _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, null);
                var fmts = new SurfaceFormatKHR[fmtCount];
                fixed (SurfaceFormatKHR* p = fmts)
                    _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, p);

                var pickFmt = fmts[0];
                foreach (var f in fmts)
                {
                    if ((f.Format == Format.B8G8R8A8Srgb || f.Format == Format.R8G8B8A8Srgb) &&
                        f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                    {
                        pickFmt = f;
                        break;
                    }
                }

                uint imageCount = Math.Max(2, caps.MinImageCount);
                if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
                    imageCount = caps.MaxImageCount;

                var preTransform = PickPreTransform(caps);

                var sci = new SwapchainCreateInfoKHR
                {
                    SType = StructureType.SwapchainCreateInfoKhr,
                    Surface = _surface,
                    MinImageCount = imageCount,
                    ImageFormat = pickFmt.Format,
                    ImageColorSpace = pickFmt.ColorSpace,
                    ImageExtent = extent,
                    ImageArrayLayers = 1,
                    ImageUsage = ImageUsageFlags.ImageUsageTransferDstBit,
                    ImageSharingMode = SharingMode.Exclusive,
                    PreTransform = preTransform,
                    CompositeAlpha = CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                    PresentMode = PresentModeKHR.PresentModeFifoKhr,
                    Clipped = true,
                    OldSwapchain = _swapchain
                };

                var result = _swapchainExtension.CreateSwapchain(_device, ref sci, null, out var newSwapchain);
                if (result != Result.Success)
                    throw new Exception($"vkCreateSwapchainKHR failed: {result}");

                // Destroy old swapchain
                if (_swapchain.Handle != 0)
                {
                    DestroyPerImageSyncObjects();
                    _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
                }
                _swapchain = newSwapchain;

                // Fetch new swapchain images
                uint count = 0;
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, null);
                _swapchainImages = new Image[count];
                _imageSerial = new ulong[count];
                fixed (Image* pImgs = _swapchainImages)
                    _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, pImgs);

                CreatePerImageSyncObjects(count);
                _swapExtent = extent;
                _imageAcquired = false;
            }
            finally
            {
                _recreatingSwapchain = false;
            }
        }

        /// <summary>
        /// Enables DirectComposition presentation mode using Vulkan/D3D11 interop.
        /// This allows XAML overlays on top of Vulkan content.
        /// </summary>
        public unsafe void EnableDirectCompositionPresentation(IDirectCompositionPresenter presenter, uint width, uint height)
        {
            if (PRESENT_DCOMP) return;

            _logger.LogDebug($"[GraphicsHost] EnableDirectCompositionPresentation: requested {width}x{height}");

            _dcompPresenter = presenter;

            // D3D11 creates the shared texture first
            var sharedHandle = presenter.CreateSharedTexture(width, height);

            // Get actual dimensions from D3D11 (in case they differ)
            _dcompWidth = presenter.Width;
            _dcompHeight = presenter.Height;

            _logger.LogDebug($"[GraphicsHost] EnableDirectCompositionPresentation: actual D3D11 size {_dcompWidth}x{_dcompHeight}, handle=0x{sharedHandle:X}");

            // Vulkan imports the D3D11 shared texture at actual size
            _vulkanExternalMemory = new VulkanExternalMemory(_vk, _instance, _device, _physicalDevice);
            _vulkanExternalMemory.ImportSharedTexture(sharedHandle, _dcompWidth, _dcompHeight, _graphicsQueueIndex);

            // Enable DirectComposition presentation
            PRESENT_DCOMP = true;
            _previewWindowOpen = true;
            _logger.LogDebug($"[GraphicsHost] DirectComposition presentation enabled at {_dcompWidth}x{_dcompHeight}");
        }

        /// <summary>
        /// Handles resize of the DirectComposition surface.
        /// </summary>
        public void HandleDirectCompositionResize(uint width, uint height)
        {
            if (width == 0 || height == 0) return;
            if (!PRESENT_DCOMP) return;

            _pendingResizeWidth = width;
            _pendingResizeHeight = height;
            _resizePending = true;

            // Cancel any pending resize
            _resizeDebounceToken?.Cancel();
            _resizeDebounceToken = new CancellationTokenSource();
            var token = _resizeDebounceToken.Token;

            // Debounce: wait 100ms after last resize before recreating
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100, token);
                    if (!token.IsCancellationRequested)
                    {
                        RecreateDirectCompositionSharedImage(_pendingResizeWidth, _pendingResizeHeight);
                        _resizePending = false;
                    }
                }
                catch (TaskCanceledException) { }
            }, token);
        }

        private unsafe void RecreateDirectCompositionSharedImage(uint width, uint height)
        {
            _recreatingSwapchain = true;
            _vk.QueueWaitIdle(_graphicsQueue);

            try
            {
                if (_vulkanExternalMemory != null && _dcompPresenter != null)
                {
                    // D3D11 resizes and recreates the shared texture
                    var newHandle = _dcompPresenter.Resize(width, height);

                    if (newHandle == 0)
                    {
                        _logger.LogWarning($"[GraphicsHost] RecreateDirectCompositionSharedImage: D3D11 returned invalid handle, skipping Vulkan import");
                        return;
                    }

                    // Get actual dimensions from D3D11 (may differ from requested if resize failed)
                    var actualWidth = _dcompPresenter.Width;
                    var actualHeight = _dcompPresenter.Height;
                    _dcompWidth = actualWidth;
                    _dcompHeight = actualHeight;

                    // Vulkan re-imports the new D3D11 texture at actual size
                    _logger.LogDebug($"[GraphicsHost] RecreateDirectCompositionSharedImage: Importing handle 0x{newHandle:X} at {actualWidth}x{actualHeight}");
                    _vulkanExternalMemory.ImportSharedTexture(newHandle, actualWidth, actualHeight, _graphicsQueueIndex);
                }

                _logger.LogDebug($"[GraphicsHost] Recreated DirectComposition shared image: {_dcompWidth}x{_dcompHeight}");
            }
            finally
            {
                _recreatingSwapchain = false;
            }
        }
#else
        // Stubs for non-Windows platforms (e.g., plain net8.0)
        public void OnPreviewWindowClosed()
        {
            PRESENT = false;
            _previewWindowOpen = false;
        }

        public void EnableWin32Presentation(nint hwnd, uint width, uint height)
        {
            // Not supported on this platform
        }

        public void HandleWin32SurfaceResize(uint width, uint height)
        {
            // Not supported on this platform
        }

        public void EnableDirectCompositionPresentation(IDirectCompositionPresenter presenter, uint width, uint height)
        {
            // Not supported on this platform
        }

        public void HandleDirectCompositionResize(uint width, uint height)
        {
            // Not supported on this platform
        }
#endif
#endif

        private unsafe bool TryPickDeviceAndQueuesPreWindow(out PhysicalDevice picked, out uint gfxQ, out uint presentQ)
        {
            picked = default; gfxQ = presentQ = uint.MaxValue;

            uint devCount = 0;
            _vk.EnumeratePhysicalDevices(_instance, ref devCount, null);
            if (devCount == 0) return false;

            var devs = new PhysicalDevice[devCount];
            fixed (PhysicalDevice* p = devs) _vk.EnumeratePhysicalDevices(_instance, &devCount, p);

            foreach (var pd in devs)
            {
                if (!DeviceHasSwapchain(pd)) continue;

                uint qCount = 0;
                _vk.GetPhysicalDeviceQueueFamilyProperties(pd, ref qCount, null);
                var qprops = new QueueFamilyProperties[qCount];
                fixed (QueueFamilyProperties* qp = qprops)
                    _vk.GetPhysicalDeviceQueueFamilyProperties(pd, ref qCount, qp);

                uint g = uint.MaxValue, pIdx = uint.MaxValue;

                for (uint i = 0; i < qCount; i++)
                {
                    if (g == uint.MaxValue && (qprops[i].QueueFlags & QueueFlags.QueueGraphicsBit) != 0)
                        g = i;

                    // This is the key call: does this family support presenting to the Win32 desktop?
                    // No VkSurfaceKHR or window handle is required.
#if WINDOWS
                    if (pIdx == uint.MaxValue && _khrWin32Surface.GetPhysicalDeviceWin32PresentationSupport(pd, i))
                        pIdx = i;
#else
                    // On non-Windows platforms, just pick any queue family for present
                    if (pIdx == uint.MaxValue)
                        pIdx = i;
#endif
                }

                if (g != uint.MaxValue && pIdx != uint.MaxValue)
                {
                    picked = pd; gfxQ = g; presentQ = pIdx;
                    return true;
                }
            }

            return false;
        }

        private unsafe bool DeviceHasSwapchain(PhysicalDevice pd)
        {
            uint count = 0;
            _vk.EnumerateDeviceExtensionProperties(pd, (byte*)null, ref count, null);
            var props = new ExtensionProperties[count];
            fixed (ExtensionProperties* p = props)
                _vk.EnumerateDeviceExtensionProperties(pd, (byte*)null, ref count, p);

            for (int i = 0; i < props.Length; i++)
            {
                fixed (byte* pExtensionName = props[i].ExtensionName)
                {
                    var name = SilkMarshal.PtrToString((nint)pExtensionName);
                    if (name == "VK_KHR_swapchain") return true;
                }
            }
            return false;
        }
        private unsafe void OnLoadHeadless()
        {
            _vk = Vk.GetApi();

            // --- Instance ---
            var appInfo = new ApplicationInfo(
                pApplicationName: (byte*)SilkMarshal.StringToPtr("Headless"),
                applicationVersion: new Version32(1, 0, 0),
                pEngineName: (byte*)SilkMarshal.StringToPtr("NoEngine"),
                engineVersion: new Version32(1, 0, 0),
                apiVersion: Vk.Version12
            );

            // Instance extensions: none needed for pure headless.
            // On macOS/MoltenVK you likely need portability enumeration:
            var instExts = new List<string>();
#if MACCATALYST
            //instExts.Add("VK_KHR_portability_enumeration");
            instExts.Add("VK_EXT_metal_surface");  // Standard extension for Metal interop
#else
            instExts.Add("VK_KHR_win32_surface");
            instExts.Add("VK_KHR_external_memory_capabilities");
            instExts.Add("VK_KHR_external_semaphore_capabilities");
#endif
            instExts.Add("VK_KHR_surface");
#if !MACCATALYST
            instExts.Add("VK_EXT_debug_utils");
#endif
            var instExtPtr = SilkMarshal.StringArrayToPtr(instExts.ToArray());
            var instFlags = InstanceCreateFlags.None;

            var layers = new List<string>();
#if MACCATALYST
            instFlags |= (InstanceCreateFlags)InstanceCreateFlags.InstanceCreateEnumeratePortabilityBitKhr;
#else
            if (IsInstanceLayerAvailable("VK_LAYER_KHRONOS_validation"))
                layers.Add("VK_LAYER_KHRONOS_validation");
            else
                _logger.LogWarning("[Vulkan] VK_LAYER_KHRONOS_validation not available — skipping validation layer");
#endif

            
            
            var layersPtr = SilkMarshal.StringArrayToPtr(layers.ToArray());

            var instanceCI = new InstanceCreateInfo(
                pApplicationInfo: &appInfo,
                flags: instFlags,
                enabledExtensionCount: (uint)instExts.Count,
                ppEnabledExtensionNames: (byte**)instExtPtr,
                enabledLayerCount: (uint)layers.Count,
                ppEnabledLayerNames: (byte**)layersPtr
            );
            var result = _vk.CreateInstance(instanceCI, null, out _instance);
            if (result != Result.Success)
            {
                _logger.LogError("Vulkan CreateInstance failed: {Result}", result);
                throw new Exception("Failed");
            }

#if !MACCATALYST
            SetupDebugMessenger();
#endif

            _khrSurface = new KhrSurface(_vk.Context);
#if WINDOWS
            _khrWin32Surface = new KhrWin32Surface(_vk.Context);
#elif MACCATALYST
            _extMetalSurface = new ExtMetalSurface(_vk.Context);
#endif

            // --- Physical device & queue family ---
            if (!TryPickDeviceAndQueuesPreWindow(out _physicalDevice, out uint graphicsQueue, out uint presentQueue))
                throw new Exception("No device has GRAPHICS and Win32 PRESENT support.");

            _graphicsQueueIndex = graphicsQueue;

            // --- Logical device ---
            float prio = 1f;
            var qCI = new DeviceQueueCreateInfo(queueFamilyIndex: graphicsQueue, queueCount: 1, pQueuePriorities: &prio);

            var devExts = new List<string>();
            devExts.Add("VK_KHR_swapchain");
            devExts.Add("VK_KHR_timeline_semaphore");
#if MACCATALYST
            // MoltenVK requires this device extension
            devExts.Add("VK_KHR_portability_subset");
#else
            // External memory extensions for DirectX interop
            devExts.Add("VK_KHR_external_memory");
            devExts.Add("VK_KHR_external_memory_win32");
            devExts.Add("VK_KHR_external_semaphore");
            devExts.Add("VK_KHR_external_semaphore_win32");
            devExts.Add("VK_KHR_dedicated_allocation");  // Required for D3D11 texture import
            devExts.Add("VK_KHR_get_memory_requirements2");  // Required for dedicated allocation
#endif
            var devExtPtr = SilkMarshal.StringArrayToPtr(devExts.ToArray());

            PhysicalDeviceVulkan12Features v12 = new()
            {
                SType = StructureType.PhysicalDeviceVulkan12Features,
                TimelineSemaphore = Vk.True // <-- REQUIRED
            };

            var devCI = new DeviceCreateInfo(
                queueCreateInfoCount: 1,
                pQueueCreateInfos: &qCI,
                enabledExtensionCount: (uint)devExts.Count,
                ppEnabledExtensionNames: (byte**)devExtPtr,
                pNext: &v12
            );
            _vk.CreateDevice(_physicalDevice, devCI, null, out _device);
            _vk.GetDeviceQueue(_device, graphicsQueue, 0, out _graphicsQueue);

            _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out _memProps);

            // --- Sync objects (double-buffered fence & semaphores reused even if headless) ---
            _imageSerial = new ulong[MAX_FRAMES_IN_FLIGHT];
            _imageAvailableSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            var fenceInfo = new FenceCreateInfo(flags: FenceCreateFlags.FenceCreateSignaledBit);
            var semInfo = new SemaphoreCreateInfo()
            {
                SType = StructureType.SemaphoreCreateInfo
            };
            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                _vk.CreateSemaphore(_device, semInfo, null, out _imageAvailableSemaphores[i]);
            }

            
            SemaphoreTypeCreateInfo timelineType = new()
            {
                SType = StructureType.SemaphoreTypeCreateInfo,
                SemaphoreType = SemaphoreType.Timeline,
                InitialValue = _timelineValue
            };
            SemaphoreCreateInfo semCI = new() { SType = StructureType.SemaphoreCreateInfo, PNext = &timelineType };

            // Create two timeline semaphores
            SemaphoreTypeCreateInfo tlType = new()
            {
                SType = StructureType.SemaphoreTypeCreateInfo,
                SemaphoreType = SemaphoreType.Timeline,
                InitialValue = 0   // both start at 0
            };
            SemaphoreCreateInfo tlCI = new()
            {
                SType = StructureType.SemaphoreCreateInfo,
                PNext = &tlType
            };
            _vk.CreateSemaphore(_device, &tlCI, null, out _gpuDoneTL);
            _vk.CreateSemaphore(_device, &tlCI, null, out _cpuDoneTL);

            var fi = new FenceCreateInfo(flags: FenceCreateFlags.FenceCreateSignaledBit);
            _vk.CreateFence(_device, fi, null, out _submitFence);

            _gpuCtr = 0;
            _cpuCtr = 0;

            // --- Command pool & a single primary CB ---
            var poolCI = new CommandPoolCreateInfo(
                queueFamilyIndex: graphicsQueue,
                flags: CommandPoolCreateFlags.CommandPoolCreateResetCommandBufferBit
            );
            _vk.CreateCommandPool(_device, poolCI, null, out _commandPool);

            // Create command buffers
            _cmds = new CommandBuffer[MAX_FRAMES_IN_FLIGHT];
            var allocInfo = new CommandBufferAllocateInfo(
                commandPool: _commandPool,
                level: CommandBufferLevel.Primary,
                commandBufferCount: (uint)MAX_FRAMES_IN_FLIGHT);
            fixed (CommandBuffer* p = _cmds)
                _vk.AllocateCommandBuffers(_device, ref allocInfo, p);

            var allocOne = new CommandBufferAllocateInfo(
                commandPool: _commandPool,
                level: CommandBufferLevel.Primary,
                commandBufferCount: 1
            );

            // --- Off-screen image (use one format consistently) ---
            var offFormat = Format.R8G8B8A8Srgb; // pick one and use it everywhere
            ImageCreateInfo imgInfo = new()
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = offFormat,
                Extent = new Extent3D(_offscreenExtent.Width, _offscreenExtent.Height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.ImageUsageColorAttachmentBit | ImageUsageFlags.ImageUsageTransferSrcBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };
            _vk.CreateImage(_device, &imgInfo, null, out _offscreenImage);
            _vk.GetImageMemoryRequirements(_device, _offscreenImage, out var reqs);
            uint memType = FindMemoryType(reqs.MemoryTypeBits, MemoryPropertyFlags.MemoryPropertyDeviceLocalBit, in _memProps);
            MemoryAllocateInfo mAI = new() { SType = StructureType.MemoryAllocateInfo, AllocationSize = reqs.Size, MemoryTypeIndex = memType };
            _vk.AllocateMemory(_device, &mAI, null, out _offscreenMemory);
            _vk.BindImageMemory(_device, _offscreenImage, _offscreenMemory, 0);

            ImageViewCreateInfo viewCI = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _offscreenImage,
                ViewType = ImageViewType.Type2D,
                Format = offFormat,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            _vk.CreateImageView(_device, &viewCI, null, out _offscreenView);

            // --- Off-screen render pass & framebuffer ---
            var att = new AttachmentDescription(
                format: offFormat,
                samples: SampleCountFlags.SampleCount1Bit,
                loadOp: AttachmentLoadOp.Clear,
                storeOp: AttachmentStoreOp.Store,
                stencilLoadOp: AttachmentLoadOp.DontCare,
                stencilStoreOp: AttachmentStoreOp.DontCare,
                initialLayout: ImageLayout.Undefined,
                finalLayout: ImageLayout.ColorAttachmentOptimal
            );
            var attRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
            var sp = new SubpassDescription(pipelineBindPoint: PipelineBindPoint.Graphics, colorAttachmentCount: 1, pColorAttachments: &attRef);
            var dep = new SubpassDependency(
                srcSubpass: Vk.SubpassExternal, dstSubpass: 0,
                srcStageMask: PipelineStageFlags.ColorAttachmentOutputBit,
                dstStageMask: PipelineStageFlags.ColorAttachmentOutputBit,
                srcAccessMask: 0,
                dstAccessMask: AccessFlags.AccessColorAttachmentWriteBit
            );
            var rpCI = new RenderPassCreateInfo(attachmentCount: 1, pAttachments: &att, subpassCount: 1, pSubpasses: &sp, dependencyCount: 1, pDependencies: &dep);
            _vk.CreateRenderPass(_device, rpCI, null, out _offscreenRenderPass);

            fixed (ImageView* pView = &_offscreenView)
            {
                FramebufferCreateInfo fbCI = new()
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = _offscreenRenderPass,
                    AttachmentCount = 1,
                    PAttachments = pView,
                    Width = _offscreenExtent.Width,
                    Height = _offscreenExtent.Height,
                    Layers = 1
                };
                _vk.CreateFramebuffer(_device, &fbCI, null, out _offscreenFramebuffer);
            }

            // --- Staging buffer for readback ---
            _stagingRing = new StagingRing(_vk, _device, _memProps, _graphicsQueueIndex, _imageSize);

            // --- Shaders, pipeline layout, pipeline (use _offscreenRenderPass) ---
            using var stream = OpenShaderFile("simple.vert.spv");
            using var streamReader = new BinaryReader(stream);
            var vertBytes = streamReader.ReadBytes(Int32.MaxValue / 2);

            using var fragStream = OpenShaderFile($"{_shader}.frag.spv");
            using var fragStreamReader = new BinaryReader(fragStream);
            var fragBytes = fragStreamReader.ReadBytes(Int32.MaxValue / 2);

            _vertShaderModule = CreateShaderModule(vertBytes);
            _fragShaderModule = CreateShaderModule(fragBytes);

            var vp = new Viewport(0, 0, _offscreenExtent.Width, _offscreenExtent.Height, 0f, 1f);
            var sc = new Rect2D(new Offset2D(0, 0), _offscreenExtent);
            var vpState = new PipelineViewportStateCreateInfo(viewportCount: 1, pViewports: &vp, scissorCount: 1, pScissors: &sc);

            // SSBO descriptor set layout (binding 0 = storage buffer, fragment stage)
            CreateSsboResources();

            var pushRange = new PushConstantRange(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<PushData>());
            fixed (DescriptorSetLayout* pSetLayout = &_ssboSetLayout)
            {
                var plCI = new PipelineLayoutCreateInfo(
                    pushConstantRangeCount: 1, pPushConstantRanges: &pushRange,
                    setLayoutCount: 1, pSetLayouts: pSetLayout);
                _vk.CreatePipelineLayout(_device, plCI, null, out _pipelineLayout);
            }

            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo(stage: ShaderStageFlags.VertexBit, module: _vertShaderModule, pName: (byte*)SilkMarshal.StringToPtr("main"));
            shaderStages[1] = new PipelineShaderStageCreateInfo(stage: ShaderStageFlags.FragmentBit, module: _fragShaderModule, pName: (byte*)SilkMarshal.StringToPtr("main"));
            

            var vi = new PipelineVertexInputStateCreateInfo(StructureType.PipelineVertexInputStateCreateInfo);
            var ia = new PipelineInputAssemblyStateCreateInfo(topology: PrimitiveTopology.TriangleList);
            var rs = new PipelineRasterizationStateCreateInfo(polygonMode: PolygonMode.Fill, cullMode: CullModeFlags.CullModeNone, frontFace: FrontFace.CounterClockwise, lineWidth: 1f);
            var ms = new PipelineMultisampleStateCreateInfo(rasterizationSamples: SampleCountFlags.SampleCount1Bit);
            var blendAtt = new PipelineColorBlendAttachmentState(blendEnable: false,
                colorWriteMask: ColorComponentFlags.ColorComponentRBit | ColorComponentFlags.ColorComponentGBit |
                                ColorComponentFlags.ColorComponentBBit | ColorComponentFlags.ColorComponentABit);
            var blend = new PipelineColorBlendStateCreateInfo(attachmentCount: 1, pAttachments: &blendAtt);

            var gpCI = new GraphicsPipelineCreateInfo(
                stageCount: 2, pStages: shaderStages,
                pVertexInputState: &vi, pInputAssemblyState: &ia,
                pViewportState: &vpState, pRasterizationState: &rs,
                pMultisampleState: &ms, pDepthStencilState: null,
                pColorBlendState: &blend, pDynamicState: null,
                layout: _pipelineLayout, renderPass: _offscreenRenderPass, subpass: 0
            );
            _vk.CreateGraphicsPipelines(_device, default, 1, gpCI, null, out _pipeline);

            CreateComputePreludes();
        }

        /// <summary>
        /// The generic compute-prelude hook: a shader that ships a {name}.comp.spv gets a
        /// simulation pass created here — at startup, never at SetShader time (runtime
        /// pipeline creation hangs MoltenVK). The pass owns all of its resources; the
        /// host only records it while its shader is active.
        /// </summary>
        private unsafe void CreateComputePreludes()
        {
            const string shader = Shaders.Fluid;
            byte[] spirv;
            try
            {
                using var stream = OpenShaderFile($"{shader}.comp.spv");
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                spirv = ms.ToArray();
            }
            catch (Exception e)
            {
                _logger.LogWarning("[GraphicsHost] No compute prelude for '{Shader}' ({Message}); the shader will render without simulation state.",
                    shader, e.Message);
                return;
            }

            _computePrelude = new FluidSimulationPass(
                _vk, _device, _memProps, _ssboSetLayout, spirv, (uint)Unsafe.SizeOf<PushData>());
            _computePrelude.WriteStateDescriptors(_frameSets, MAX_FRAMES_IN_FLIGHT);
            _computePreludeShader = shader;
            _logger.LogDebug("[GraphicsHost] Compute prelude created for shader '{Shader}'.", shader);
        }

        private unsafe bool IsInstanceLayerAvailable(string layerName)
        {
            uint count = 0;
            _vk.EnumerateInstanceLayerProperties(ref count, null);
            var props = new LayerProperties[count];
            fixed (LayerProperties* p = props)
                _vk.EnumerateInstanceLayerProperties(ref count, p);
            foreach (var prop in props)
            {
                if (SilkMarshal.PtrToString((nint)prop.LayerName) == layerName)
                    return true;
            }
            return false;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static unsafe Bool32 DebugCallback(
            DebugUtilsMessageSeverityFlagsEXT severity,
            DebugUtilsMessageTypeFlagsEXT types,
            DebugUtilsMessengerCallbackDataEXT* callbackData,
            void* userData)
        {
            string msg = SilkMarshal.PtrToString((nint)callbackData->PMessage);
            _staticLogger?.LogWarning($"[Vulkan] {severity}: {msg}");
            return Vk.False; // or new Bool32(0)
        }

        private ExtDebugUtils _debugUtils;
        private DebugUtilsMessengerEXT _debugMessenger;

        private unsafe void DCompPresentAsync(IDirectCompositionPresenter presenter, ulong gpuValue)
        {
            ThreadPool.UnsafeQueueUserWorkItem(state =>
            {
                var (p, val) = ((IDirectCompositionPresenter, ulong))state!;
                try
                {
                    var sem = _gpuDoneTL;
                    var v = val;
                    SemaphoreWaitInfo wi = new()
                    {
                        SType = StructureType.SemaphoreWaitInfo,
                        SemaphoreCount = 1,
                        PSemaphores = &sem,
                        PValues = &v
                    };
                    _vk.WaitSemaphores(_device, &wi, ulong.MaxValue);
                    p.CopyAndPresent(0, 0);
                }
                finally
                {
                    #if WINDOWS
                    _presentInProgress = false;
                    #endif
                }
            }, (presenter, gpuValue));
        }

        private unsafe void SetupDebugMessenger()
        {
            _debugUtils = new ExtDebugUtils(_vk.Context);

            var createInfo = new DebugUtilsMessengerCreateInfoEXT
            {
                SType = StructureType.DebugUtilsMessengerCreateInfoExt,
                MessageSeverity =
                    DebugUtilsMessageSeverityFlagsEXT.DebugUtilsMessageSeverityVerboseBitExt |
                    DebugUtilsMessageSeverityFlagsEXT.DebugUtilsMessageSeverityWarningBitExt |
                    DebugUtilsMessageSeverityFlagsEXT.DebugUtilsMessageSeverityErrorBitExt,
                MessageType =
                    DebugUtilsMessageTypeFlagsEXT.DebugUtilsMessageTypeGeneralBitExt |
                    DebugUtilsMessageTypeFlagsEXT.DebugUtilsMessageTypeValidationBitExt |
                    DebugUtilsMessageTypeFlagsEXT.DebugUtilsMessageTypePerformanceBitExt,
                PfnUserCallback = (delegate* unmanaged[Cdecl]
                    <DebugUtilsMessageSeverityFlagsEXT,
                     DebugUtilsMessageTypeFlagsEXT,
                     DebugUtilsMessengerCallbackDataEXT*,
                     void*, Bool32>)&DebugCallback
            };

            if (_debugUtils.CreateDebugUtilsMessenger(_instance, in createInfo, null, out _debugMessenger) != Result.Success)
                throw new Exception("Failed to create debug messenger");
        }

        /// <summary>Advances the pad-colour state to this frame — fade-ins, fade-outs,
        /// the cross-fade snapshot when the set changes — and writes it to this frame's
        /// colour SSBO. Runs before the spectrum upload so the injection table sees
        /// the same frame's colours: a press lands in the frame that observes it.</summary>
        private unsafe void UpdateColorState(double elapsed)
        {
            var targetColors = Colors;

            // --- Update effective color list (handles fade-in and fade-out) ---
            // Mark colors that have been removed
            for (int i = 0; i < _effectiveColors.Count; i++)
            {
                var e = _effectiveColors[i];
                if (e.RemovedTime > double.NegativeInfinity) continue;
                bool stillPresent = false;
                for (int j = 0; j < targetColors.Length; j++)
                    if (targetColors[j] == e.Color) { stillPresent = true; break; }
                if (!stillPresent)
                {
                    e.RemovedTime = elapsed;
                    _effectiveColors[i] = e;
                    _logger.LogDebug("[Pads] Colour #{R:X2}{G:X2}{B:X2} released at {T:F2}s.", e.Color.R, e.Color.G, e.Color.B, elapsed);
                }
            }

            // Remove fully faded-out slots — but only once any cross-fade transition
            // is also idle.  Removing a color changes currentCount, which changes the
            // regionId % currentCount mapping in the shader and causes a visible snap
            // if a transition is still in flight.  Leaving the entry in the list while
            // its w=0.0 is harmless (it contributes no color) and keeps the mapping
            // stable until the cross-fade completes.
            bool anyRemoved = false;
            for (int i = _effectiveColors.Count - 1; i >= 0; i--)
            {
                var e = _effectiveColors[i];
                if (e.RemovedTime > double.NegativeInfinity && (elapsed - e.RemovedTime) >= COLOR_FADE_DURATION_SECS)
                {
                    float currentTransitionT = (float)Math.Clamp(
                        (elapsed - _transitionStart) / COLOR_FADE_DURATION_SECS, 0.0, 0.999);
                    bool transitionIdle = currentTransitionT >= 0.999f || _snapshotColors.Count == 0;

                    if (!transitionIdle)
                        continue; // cross-fade still running — defer removal to avoid region snap

                    if (!anyRemoved)
                    {
                        _snapshotColors = BuildCurrentColorVectors(elapsed);
                        _transitionStart = elapsed;
                    }
                    anyRemoved = true;
                    _effectiveColors.RemoveAt(i);
                }
            }

            // Add newly appeared colors — take a single snapshot before any additions
            bool anyAdded = false;
            for (int j = 0; j < targetColors.Length; j++)
            {
                var c = targetColors[j];
                bool alreadyTracked = false;
                for (int i = 0; i < _effectiveColors.Count; i++)
                    if (_effectiveColors[i].Color == c && _effectiveColors[i].RemovedTime <= double.NegativeInfinity)
                    { alreadyTracked = true; break; }

                if (!alreadyTracked)
                {
                    // Re-add a color that's still fading out — rehabilitate it
                    bool rehabilitated = false;
                    for (int i = 0; i < _effectiveColors.Count; i++)
                    {
                        var e = _effectiveColors[i];
                        if (e.Color == c && e.RemovedTime > double.NegativeInfinity)
                        {
                            // Adjust AddedTime so fade-in continues from current fade-out level
                            double currentFadeOut = Math.Clamp(1.0 - (elapsed - e.RemovedTime) / COLOR_FADE_DURATION_SECS, 0.0, 1.0);
                            double equivalentFadeInAge = currentFadeOut * COLOR_FADE_DURATION_SECS;
                            // Re-added at the end: the list is in press order, and the
                            // injection gives disc 0 the newest.
                            _effectiveColors.RemoveAt(i);
                            _effectiveColors.Add(new EffectiveColor
                            {
                                Color = c,
                                AddedTime = elapsed - equivalentFadeInAge,
                                RemovedTime = double.NegativeInfinity
                            });
                            rehabilitated = true;
                            break;
                        }
                    }

                    if (!rehabilitated)
                    {
                        if (!anyAdded && !anyRemoved)
                        {
                            float T = (float)Math.Clamp(
                                (elapsed - _transitionStart) / COLOR_FADE_DURATION_SECS, 0.0, 1.0);

                            // If the last snapshot was taken very recently (T < 15% ≈ 300ms),
                            // skip resetting the snapshot and let this color join the existing
                            // cross-fade. This batches rapid simultaneous additions (e.g. four
                            // pad presses at once) into one smooth transition instead of causing
                            // four cascading sector reshuffles.
                            bool shouldSnapshot = _snapshotColors.Count == 0 || T >= 0.15f;
                            if (shouldSnapshot)
                            {
                                // Snapshot the visually-blended state rather than raw weights.
                                // If a cross-fade is in progress, the shader shows:
                                //   mix(snapshot[R % prevCount], current[R % currCount], T)
                                // Taking a raw snapshot and resetting T=0 would snap each region
                                // to its raw weight, diverging from the blended display.
                                // Evaluate the blend per-slot so T=0 is visually continuous.
                                var raw = BuildCurrentColorVectors(elapsed);
                                if (_snapshotColors.Count > 0 && T < 0.999f)
                                {
                                    var blendedSnap = new List<Vector4>(raw.Count);
                                    for (int s = 0; s < raw.Count; s++)
                                    {
                                        var prev = _snapshotColors[s % _snapshotColors.Count];
                                        var cur  = raw[s];
                                        blendedSnap.Add(new Vector4(
                                            prev.X + (cur.X - prev.X) * T,
                                            prev.Y + (cur.Y - prev.Y) * T,
                                            prev.Z + (cur.Z - prev.Z) * T,
                                            prev.W + (cur.W - prev.W) * T));
                                    }
                                    _snapshotColors = blendedSnap;
                                }
                                else
                                {
                                    _snapshotColors = raw;
                                }
                                _transitionStart = elapsed;
                            }
                        }
                        anyAdded = true;
                        _effectiveColors.Add(new EffectiveColor { Color = c, AddedTime = elapsed, RemovedTime = double.NegativeInfinity });
                        _logger.LogDebug("[Pads] Colour #{R:X2}{G:X2}{B:X2} held from {T:F2}s (hue {Hue:F3}); regions {Regions}.",
                            c.R, c.G, c.B, elapsed, HueOf(new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f)), ColorRegions ? "on" : "off");
                    }
                }
            }

            // Build current color vectors for SSBO
            var currentColors = BuildCurrentColorVectors(elapsed);

            float transitionT = (float)Math.Clamp(
                (elapsed - _transitionStart) / COLOR_FADE_DURATION_SECS, 0.0, 0.999);

            // Write SSBO for this frame slot — empty while the colour regions are off, so
            // the shaders paint nothing in the pad colours; the injection below still sees them.
            if (ColorRegions)
            {
                WriteSsboData(_currentFrame, currentColors, _snapshotColors, transitionT);
            }
            else
            {
                WriteSsboData(_currentFrame, NoColors, NoColors, 0f);
            }

            UpdateDiscColors(elapsed);
        }

        /// <summary>The injection's view of the pads: which disc is held in what colour
        /// (<see cref="PadColors"/>), snapping in on the press — a burst, not a ramp — and
        /// remembered for <see cref="PAD_INJECT_FADE_OUT_SECS"/> after the release so the
        /// flood a tap leaves behind stays in its colour to the end of its envelope.</summary>
        private void UpdateDiscColors(double elapsed)
        {
            var pads = PadColors;
            for (int disc = 0; disc < InjectionSites; disc++)
            {
                bool held = false;
                var color = default(System.Drawing.Color);
                for (int i = 0; i < pads.Length; i++)
                {
                    if (pads[i].Disc == disc)
                    {
                        held = true;
                        color = pads[i].Color;      // the last listed wins: pads sharing a disc, newest last
                    }
                }
                if (held)
                {
                    if (!_discHeld[disc] || color != _discColor[disc])
                    {
                        _discHeldAt[disc] = elapsed;
                        _discColor[disc] = color;
                        _discHue[disc] = HueOf(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1f));
                        _logger.LogDebug("[Pads] Disc {Disc} takes #{R:X2}{G:X2}{B:X2} (hue {Hue:F3}) at {T:F2}s.", disc, color.R, color.G, color.B, _discHue[disc], elapsed);
                    }
                }
                else if (_discHeld[disc])
                {
                    _discReleasedAt[disc] = elapsed;
                }
                _discHeld[disc] = held;
            }
        }

        // Build current-frame color vectors from effective color list
        private List<Vector4> BuildCurrentColorVectors(double elapsed,
            double fadeInSeconds = COLOR_FADE_DURATION_SECS, double fadeOutSeconds = COLOR_FADE_DURATION_SECS)
        {
            var list = new List<Vector4>(_effectiveColors.Count);
            foreach (var e in _effectiveColors)
            {
                float fadeIn  = (float)Math.Clamp((elapsed - e.AddedTime) / fadeInSeconds, 0.0, 1.0);
                float fadeOut = e.RemovedTime > double.NegativeInfinity
                    ? (float)Math.Clamp(1.0 - (elapsed - e.RemovedTime) / fadeOutSeconds, 0.0, 1.0)
                    : 1.0f;
                // Normalize 0-255 → 0-1
                list.Add(new Vector4(e.Color.R / 255f, e.Color.G / 255f, e.Color.B / 255f, fadeIn * fadeOut));
            }
            return list;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // SSBO helpers
        // ──────────────────────────────────────────────────────────────────────────

        // SSBO header layout (std430, matches GLSL ColorState header)
        [StructLayout(LayoutKind.Sequential)]
        private struct ColorStateHeader
        {
            public int   CurrentCount;
            public int   PreviousCount;
            public float TransitionT;
            private float _pad;
        }

        private unsafe void CreateSsboResources()
        {
            // Descriptor set layout — a superset of what any one shader declares (shader
            // bindings must be a subset of the layout, so existing shaders that use only
            // binding 0 are untouched):
            //   0 ColorState (fragment)     1 Spectrum (fragment|compute)
            //   2 sim state SRC             3 sim state DST (both fragment|compute)
            var bindings = stackalloc DescriptorSetLayoutBinding[4];
            bindings[0] = new DescriptorSetLayoutBinding
            {
                Binding            = 0,
                DescriptorType     = DescriptorType.StorageBuffer,
                DescriptorCount    = 1,
                StageFlags         = ShaderStageFlags.FragmentBit,
            };
            for (uint b = 1; b < 4; b++)
            {
                bindings[b] = new DescriptorSetLayoutBinding
                {
                    Binding            = b,
                    DescriptorType     = DescriptorType.StorageBuffer,
                    DescriptorCount    = 1,
                    StageFlags         = ShaderStageFlags.FragmentBit | ShaderStageFlags.ComputeBit,
                };
            }
            var dslCI = new DescriptorSetLayoutCreateInfo
            {
                SType        = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 4,
                PBindings    = bindings,
            };
            _vk.CreateDescriptorSetLayout(_device, dslCI, null, out _ssboSetLayout);

            // Descriptor pool: one set per (frame slot, ping-pong parity), 4 buffers each.
            var poolSize = new DescriptorPoolSize
            {
                Type            = DescriptorType.StorageBuffer,
                DescriptorCount = (uint)(MAX_FRAMES_IN_FLIGHT * 2 * 4),
            };
            var poolCI = new DescriptorPoolCreateInfo
            {
                SType         = StructureType.DescriptorPoolCreateInfo,
                MaxSets       = (uint)(MAX_FRAMES_IN_FLIGHT * 2),
                PoolSizeCount = 1,
                PPoolSizes    = &poolSize,
            };
            _vk.CreateDescriptorPool(_device, poolCI, null, out _ssboPool);

            // One buffer + descriptor set per frame-in-flight slot
            // Max size: header (16) + 64 entries * 16 bytes each = 1040 bytes; allocate 4 KB
            const uint bufferSize = 4096;
            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                var bufCI = new BufferCreateInfo
                {
                    SType       = StructureType.BufferCreateInfo,
                    Size        = bufferSize,
                    Usage       = BufferUsageFlags.StorageBufferBit,
                    SharingMode = SharingMode.Exclusive,
                };
                _vk.CreateBuffer(_device, bufCI, null, out _ssboBuffers[i]);

                _vk.GetBufferMemoryRequirements(_device, _ssboBuffers[i], out var memReq);
                var allocInfo = new MemoryAllocateInfo
                {
                    SType           = StructureType.MemoryAllocateInfo,
                    AllocationSize  = memReq.Size,
                    MemoryTypeIndex = FindMemoryType(
                        memReq.MemoryTypeBits,
                        MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                        _memProps),
                };
                _vk.AllocateMemory(_device, allocInfo, null, out _ssboMemories[i]);
                _vk.BindBufferMemory(_device, _ssboBuffers[i], _ssboMemories[i], 0);

                void* mapped;
                _vk.MapMemory(_device, _ssboMemories[i], 0, bufferSize, 0, &mapped);
                _ssboMapped[i] = mapped;
            }

            // Spectrum buffers: same shape as ColorState (per frame slot, host-visible,
            // persistently mapped, memcpy'd each frame). Header zeroed once so a shader
            // reading before the first publish sees binCount=1024, version=0, silence.
            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                var bufCI = new BufferCreateInfo
                {
                    SType       = StructureType.BufferCreateInfo,
                    Size        = SpectrumBufferBytes,
                    Usage       = BufferUsageFlags.StorageBufferBit,
                    SharingMode = SharingMode.Exclusive,
                };
                _vk.CreateBuffer(_device, bufCI, null, out _spectrumBuffers[i]);
                _vk.GetBufferMemoryRequirements(_device, _spectrumBuffers[i], out var memReq);
                var allocInfo = new MemoryAllocateInfo
                {
                    SType           = StructureType.MemoryAllocateInfo,
                    AllocationSize  = memReq.Size,
                    MemoryTypeIndex = FindMemoryType(
                        memReq.MemoryTypeBits,
                        MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                        _memProps),
                };
                _vk.AllocateMemory(_device, allocInfo, null, out _spectrumMemories[i]);
                _vk.BindBufferMemory(_device, _spectrumBuffers[i], _spectrumMemories[i], 0);

                void* mapped;
                _vk.MapMemory(_device, _spectrumMemories[i], 0, SpectrumBufferBytes, 0, &mapped);
                _spectrumMapped[i] = mapped;
                new Span<byte>(mapped, (int)SpectrumBufferBytes).Clear();
                *(int*)mapped = SpectrumBins;                   // header.binCount
                *((float*)mapped + 2) = 93.75f;                 // header.hopHz
                FillHomeSites(new Span<float>((byte*)mapped + 16 + SpectrumBins * sizeof(float), InjectionTableFloats));
            }

            // Allocate and write descriptor sets: one per (frame slot, parity).
            const int totalSets = MAX_FRAMES_IN_FLIGHT * 2;
            var layouts = stackalloc DescriptorSetLayout[totalSets];
            for (int i = 0; i < totalSets; i++) layouts[i] = _ssboSetLayout;
            fixed (DescriptorSet* pSets = _frameSets)
            {
                var dsAllocInfo = new DescriptorSetAllocateInfo
                {
                    SType              = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool     = _ssboPool,
                    DescriptorSetCount = (uint)totalSets,
                    PSetLayouts        = layouts,
                };
                _vk.AllocateDescriptorSets(_device, dsAllocInfo, pSets);
            }

            for (int i = 0; i < totalSets; i++)
            {
                int slot = i / 2;
                var colorInfo = new DescriptorBufferInfo
                {
                    Buffer = _ssboBuffers[slot],
                    Offset = 0,
                    Range  = bufferSize,
                };
                var spectrumInfo = new DescriptorBufferInfo
                {
                    Buffer = _spectrumBuffers[slot],
                    Offset = 0,
                    Range  = SpectrumBufferBytes,
                };
                var writes = stackalloc WriteDescriptorSet[4];
                writes[0] = new WriteDescriptorSet
                {
                    SType           = StructureType.WriteDescriptorSet,
                    DstSet          = _frameSets[i],
                    DstBinding      = 0,
                    DescriptorType  = DescriptorType.StorageBuffer,
                    DescriptorCount = 1,
                    PBufferInfo     = &colorInfo,
                };
                writes[1] = new WriteDescriptorSet
                {
                    SType           = StructureType.WriteDescriptorSet,
                    DstSet          = _frameSets[i],
                    DstBinding      = 1,
                    DescriptorType  = DescriptorType.StorageBuffer,
                    DescriptorCount = 1,
                    PBufferInfo     = &spectrumInfo,
                };
                // Bindings 2/3 get valid placeholder descriptors (never accessed by
                // shaders that don't declare them); a compute prelude overwrites them
                // with its state buffers at startup.
                writes[2] = new WriteDescriptorSet
                {
                    SType           = StructureType.WriteDescriptorSet,
                    DstSet          = _frameSets[i],
                    DstBinding      = 2,
                    DescriptorType  = DescriptorType.StorageBuffer,
                    DescriptorCount = 1,
                    PBufferInfo     = &spectrumInfo,
                };
                writes[3] = new WriteDescriptorSet
                {
                    SType           = StructureType.WriteDescriptorSet,
                    DstSet          = _frameSets[i],
                    DstBinding      = 3,
                    DescriptorType  = DescriptorType.StorageBuffer,
                    DescriptorCount = 1,
                    PBufferInfo     = &spectrumInfo,
                };
                _vk.UpdateDescriptorSets(_device, 4, writes, 0, null);
            }
        }

        /// <summary>
        /// Copies the newest published spectrum into this frame slot's mapped buffer:
        /// header {binCount, version, hopHz, dropSeconds}, the magnitudes, then the
        /// injection sites. One 4 KB
        /// memcpy per frame; the header's version lets a shader detect "never published".
        /// </summary>
        private unsafe void WriteSpectrumData(int frameSlot)
        {
            var holder = Volatile.Read(ref _spectrum);
            byte* ptr = (byte*)_spectrumMapped[frameSlot];
            *(int*)ptr = SpectrumBins;
            *((int*)ptr + 1) = holder.Version;
            *((float*)ptr + 2) = 93.75f;
            *((float*)ptr + 3) = Volatile.Read(ref _dropSignal);
            fixed (float* pBins = holder.Bins)
            {
                System.Buffer.MemoryCopy(pBins, ptr + 16,
                    SpectrumPayloadFloats * sizeof(float), SpectrumPayloadFloats * sizeof(float));
            }

            // Pad colors: while any are held on the control surface, the injection
            // points take their hues from them in turn — pad 1 colors the first point,
            // pad 2 the second, round again if there are more points than pads —
            // from the same entries the kaleidoscope's regions read, on the pad fades
            // (PAD_INJECT_FADE_IN/OUT_SECS; see PadHueFor), from this same frame's state.
            // With none held the analysis palette's hues stand. The hold also
            // flattens each point's per-ring hue spread to its pad's color, since a
            // pad's red over a palette's pink, both spread a fifth of the wheel, was
            // indistinguishable. The marbling is left alone: it used to fade out with
            // the hold as well, and once the pads floods the fluid a press visibly
            // drained the marbling and the release brought it back.
            float* hues = (float*)(ptr + 16) + SpectrumBins + InjectionSites * 2;
            float hold = 0f;
            for (int i = 0; i < InjectionSites; i++)
            {
                hues[i] = PadHueFor(i, hues[i], out float weight);
                hold = Math.Max(hold, weight);
            }
            *((float*)(ptr + 16) + SpectrumBins + InjectionSites * 4 + 3) = hold;   // padHold
        }

        /// <summary>The hue injection disc <paramref name="band"/> should use: its pad's
        /// colour outright while the pad is down, that colour fading back to the palette's
        /// <paramref name="paletteHue"/> over <see cref="PAD_INJECT_FADE_OUT_SECS"/> after the
        /// release, and the palette's otherwise. <paramref name="weightOut"/> is how much
        /// of the pad colour is in force, 0..1.</summary>
        private float PadHueFor(int band, float paletteHue, out float weightOut)
        {
            double elapsed = _stopwatch.Elapsed.TotalSeconds;
            float weight;
            if (_discHeld[band])
            {
                weight = 1f;
            }
            else if (_discReleasedAt[band] > double.NegativeInfinity)
            {
                weight = (float)Math.Clamp(1.0 - (elapsed - _discReleasedAt[band]) / PAD_INJECT_FADE_OUT_SECS, 0.0, 1.0);
            }
            else
            {
                weight = 0f;
            }
            weightOut = weight;
            return weight > 0f ? HueLerp(paletteHue, _discHue[band], weight) : paletteHue;
        }

        private static float HueOf(Vector4 c)
        {
            float max = Math.Max(c.X, Math.Max(c.Y, c.Z));
            float min = Math.Min(c.X, Math.Min(c.Y, c.Z));
            float delta = max - min;
            if (delta < 1e-6f) return 0f;
            float h;
            if (max == c.X) h = ((c.Y - c.Z) / delta + 6f) % 6f;
            else if (max == c.Y) h = (c.Z - c.X) / delta + 2f;
            else h = (c.X - c.Y) / delta + 4f;
            return h / 6f;
        }

        /// <summary>Hue interpolation the short way round the wheel.</summary>
        private static float HueLerp(float h, float target, float s)
        {
            float d = target - h;
            if (d > 0.5f) d -= 1f;
            if (d < -0.5f) d += 1f;
            float r = (h + d * s) % 1f;
            return r < 0f ? r + 1f : r;
        }

        private unsafe void WriteSsboData(int frameSlot, List<Vector4> current, List<Vector4> previous, float transitionT)
        {
            byte* ptr = (byte*)_ssboMapped[frameSlot];

            // Write header
            var header = new ColorStateHeader
            {
                CurrentCount  = current.Count,
                PreviousCount = previous.Count,
                TransitionT   = transitionT,
            };
            *(ColorStateHeader*)ptr = header;
            ptr += sizeof(ColorStateHeader); // 16 bytes

            // Write current color entries
            for (int i = 0; i < current.Count; i++)
            {
                *(Vector4*)ptr = current[i];
                ptr += sizeof(Vector4);
            }
            // Write snapshot (previous) entries
            for (int i = 0; i < previous.Count; i++)
            {
                *(Vector4*)ptr = previous[i];
                ptr += sizeof(Vector4);
            }
        }

        private unsafe void DestroySsboResources()
        {
            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                if (_ssboMapped[i] != null)
                {
                    _vk.UnmapMemory(_device, _ssboMemories[i]);
                    _ssboMapped[i] = null;
                }
                if (_ssboBuffers[i].Handle != 0)
                {
                    _vk.DestroyBuffer(_device, _ssboBuffers[i], null);
                    _ssboBuffers[i] = default;
                }
                if (_ssboMemories[i].Handle != 0)
                {
                    _vk.FreeMemory(_device, _ssboMemories[i], null);
                    _ssboMemories[i] = default;
                }
            }
            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                if (_spectrumMapped[i] != null)
                {
                    _vk.UnmapMemory(_device, _spectrumMemories[i]);
                    _spectrumMapped[i] = null;
                }
                if (_spectrumBuffers[i].Handle != 0)
                {
                    _vk.DestroyBuffer(_device, _spectrumBuffers[i], null);
                    _spectrumBuffers[i] = default;
                }
                if (_spectrumMemories[i].Handle != 0)
                {
                    _vk.FreeMemory(_device, _spectrumMemories[i], null);
                    _spectrumMemories[i] = default;
                }
            }
            _computePrelude?.Destroy();
            _computePrelude = null;
            if (_ssboPool.Handle != 0)
            {
                _vk.DestroyDescriptorPool(_device, _ssboPool, null);
                _ssboPool = default;
            }
            if (_ssboSetLayout.Handle != 0)
            {
                _vk.DestroyDescriptorSetLayout(_device, _ssboSetLayout, null);
                _ssboSetLayout = default;
            }
        }

        internal static uint FindMemoryType(
            uint typeFilter,
            MemoryPropertyFlags properties,
            in PhysicalDeviceMemoryProperties memProps)
        {
            for (uint i = 0; i < memProps.MemoryTypeCount; i++)
            {
                bool typeSupported = (typeFilter & (1u << (int)i)) != 0;
                bool hasProps = (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties;

                if (typeSupported && hasProps)
                    return i;
            }

            throw new Exception("Failed to find suitable memory type!");
        }

        private unsafe void BeginSwapchainRecreate()
        {
            _recreatingSwapchain = true;     // stop OnRender from submitting
            _imageAcquired = false;          // we won’t reuse any previously acquired image
            _acquiredFrameIndex = -1;
            _vk.QueueWaitIdle(_graphicsQueue); // <<< CRITICAL: drain all in-flight work/presents
                                               // You can use _vk.DeviceWaitIdle(_device) if you prefer to be extra safe.
        }

        private void EndSwapchainRecreate()
        {
            _recreatingSwapchain = false;
        }

        private unsafe void OnRender(double obj)
        {
            if (_recreatingSwapchain || _isClosing || _tearingDownWSI)
            {
                return;
            }
            // Skip presentation if resize is pending (swapchain size doesn't match window)
            // Backing off after a stall: while the window is minimised, asking for a drawable costs
            // a blocking call into Core Animation, so try once a second instead of every frame. The
            // frame still renders and the rig still gets its pixels either way.
            var present = PRESENT && _presentationEnabled && !_resizePending && _stopwatch.Elapsed >= _presentRetryAfter;
            if (PRESENT && !present && _stopwatch.Elapsed < _presentRetryAfter)
            {
                NotePresentationSuspended("backing off after a stalled present");
            }
            if (PRESENT && !_loggedPresentStart)
            {
                _loggedPresentStart = true;
                _logger.LogDebug("[GraphicsHost] Presentation path active (resizePending={ResizePending}, presenting={Presenting}).",
                    _resizePending, present);
            }
            if (PRESENT && _resizePending)
            {
                NotePresentationSuspended("a resize is pending");
            }
            try
            {
            // Decide ring slot we're writing this frame:
            // If ring is full, we skip staging copy but still render and present
            int slot = _stagingRing.WriteIndex;
            bool canDoStagingCopy = true;
            if (Volatile.Read(ref _slotReadyGpuValue[slot]) != 0)
            {
                int free = -1;
                for (int i = 0; i < StagingRing.RingSize; i++)
                {
                    int s = (slot + i) % StagingRing.RingSize;
                    if (Volatile.Read(ref _slotReadyGpuValue[s]) == 0) { free = s; break; }
                }
                if (free < 0)
                {
                    // Ring full - skip staging copy but continue with render and presentation
                    canDoStagingCopy = false;
                }
                else
                {
                    _stagingRing.WriteIndex = free;
                    slot = free;
                }
            }

            var cmd = _cmds[_currentFrame];

            // If we're presenting, acquire a swapchain image early
            uint imageIndex = 0;
            if (PRESENT && !_isClosing)
            {
                // Swapchain not ready — mid-rebuild, or torn down. Render without presenting rather
                // than dropping the frame: the rig is fed from the offscreen image either way.
                if (_swapchain.Handle == 0 || _swapchainImages == null || _swapchainImages.Length == 0)
                {
                    NotePresentationSuspended("the swapchain is not ready");
                    present = false;
                }
                // If we don't already own an image, try to acquire one with finite timeout
                else if (!_imageAcquired)
                {
                    // Timed: the timeout below is zero, but MoltenVK still has to ask Core Animation
                    // for a drawable, and that call blocks while the window is minimised.
                    var acquireStarted = _stopwatch.Elapsed;
                    var acq = _swapchainExtension.AcquireNextImage(
                        _device, _swapchain,
                        0ul, // <-- finite timeout: non-blocking
                        _imageAvailableSemaphores[_currentFrame],
                        default,
                        ref imageIndex);

                    var acquireTook = _stopwatch.Elapsed - acquireStarted;
                    if (acquireTook >= PresentStallThreshold)
                    {
                        NotePresentStall("AcquireNextImage", acquireTook);
                    }

                    // Whatever the window is doing, this frame still renders offscreen and still
                    // feeds the rig — only the presenting is skipped. The LEDs sample the offscreen
                    // image, and they must not go dark because nobody is looking at the preview.
                    if (acq == Result.NotReady || acq == Result.Timeout)
                    {
                        // Minimised, occluded, or on another Space: no drawable to be had.
                        present = false;
                        NotePresentationSuspended($"no drawable available ({acq})");
                    }
                    else if (acq == Result.ErrorOutOfDateKhr && !_isClosing)
                    {
                        BeginSwapchainRecreate();
#if WINDOWS
                        RecreateWin32Swapchain(_win32SurfaceWidth, _win32SurfaceHeight);
#elif MACCATALYST
                        RebuildSwapchainAtPinnedSize();
#endif
                        EndSwapchainRecreate();
                        present = false;
                    }
                    else
                    {
                        // Success or Suboptimal — suboptimal is the steady state with a pinned
                        // drawable, since the swapchain is deliberately not the layer's size.
                        NotePresentationResumed();
                        _imageAcquired = true;
                        _acquiredImageIndex = imageIndex;
                        _acquiredFrameIndex = _currentFrame;

                        if (_imageSerial[imageIndex] != 0)
                        {
                            var wi = new SemaphoreWaitInfo { SType = StructureType.SemaphoreWaitInfo, SemaphoreCount = 1 };
                            fixed (Semaphore* p = &_gpuDoneTL)
                            {
                                fixed (ulong* imageSerial = &_imageSerial[imageIndex])
                                {
                                    wi.PSemaphores = p;
                                    wi.PValues = imageSerial;
                                    _vk.WaitSemaphores(_device, &wi, ulong.MaxValue);
                                }
                            }
                        }
                    }
                }
                else if (_acquiredFrameIndex >= 0)
                {
                    // Reuse the one we already acquired previously (e.g., last frame aborted before
                    // present, or presentation was paused between the acquire and now).
                    imageIndex = _acquiredImageIndex;
                }
                else
                {
                    // Held an image but lost track of which semaphore goes with it. There is nothing
                    // safe to wait on, so render for the rig and let the next frame acquire afresh.
                    _imageAcquired = false;
                    present = false;
                }
            }

            // 1) Ensure last submit finished before touching this CB.
            //
            // Timed, because this is where a starved presentation shows up: if the previous frame
            // was submitted waiting on a swapchain image that never arrived — a minimised window
            // withholding its drawable — that submission never executes, and this wait is where the
            // render loop, and with it the rig, stops.
            var fenceWaitStarted = _stopwatch.Elapsed;
            _vk.WaitForFences(_device, 1, in _submitFence, true, ulong.MaxValue);
            var fenceWaitTook = _stopwatch.Elapsed - fenceWaitStarted;
            if (fenceWaitTook >= PresentStallThreshold)
            {
                NotePresentStall("WaitForFences (previous submit)", fenceWaitTook);
            }

            // Record commands
            _vk.ResetCommandBuffer(cmd, 0);

            CommandBufferBeginInfo begin = new() { SType = StructureType.CommandBufferBeginInfo };
            _vk.BeginCommandBuffer(cmd, &begin);

            double elapsed = _stopwatch.Elapsed.TotalSeconds;

            // Push data is built before the render pass because a compute prelude (a
            // simulation stage shipped with the active shader) consumes the same block.
            var audio = Volatile.Read(ref _audioLite).Value;
            PushData pc = new()
            {
                Resolution = new System.Numerics.Vector2(_offscreenExtent.Width, _offscreenExtent.Height),
                Time = (float)elapsed * 0.15f,
                DistortionStrength = DistortionStrength,
                HueTolerance = 0.35f,
                Falloff = 0.6f,
                Param1 = ShaderParam1,
                Param2 = ShaderParam2,
                Param3 = ShaderParam3,
                Param4 = ShaderParam4,
                AudioEnergy = audio.Energy,
                AudioImpulse = audio.Impulse,
                AudioTension = audio.Tension,
                AudioPhase = audio.Phase,
                AudioHue = audio.Hue,
                AudioGrain = audio.Grain,
                AudioMotion = audio.Motion,
                AudioSpread = audio.Spread,
            };

            // Upload the newest spectrum for this frame slot (shaders that don't declare
            // binding 1 never read it), then record the active shader's compute prelude —
            // its barriers must sit OUTSIDE the render pass. Record runs several ping-pong
            // substeps and returns the LAST substep's parity: that parity's descriptor set
            // has binding 3 pointing at the buffer the final substep wrote, which is what
            // the graphics pass must read.
            UpdateColorState(elapsed);
            WriteSpectrumData(_currentFrame);
            int preludeParity = 0;
            if (_computePrelude != null && _shader == _computePreludeShader)
            {
                preludeParity = _computePrelude.Record(cmd, in pc,
                    _frameSets[_currentFrame * 2],
                    _frameSets[_currentFrame * 2 + 1]);
            }

            // --- Off-screen render pass ---
            var clear = new ClearValue(new ClearColorValue(0.1f, 0.1f, 0.1f, 1f));
            RenderPassBeginInfo rpBegin = new()
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _offscreenRenderPass,
                Framebuffer = _offscreenFramebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), _offscreenExtent),
                ClearValueCount = 1,
                PClearValues = &clear
            };

            _vk.CmdBeginRenderPass(cmd, &rpBegin, SubpassContents.Inline);
            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);

            // Bind this frame's descriptor set (parity-matched to the compute prelude
            // when one ran; parity 0 otherwise — same set layout, so the set is
            // compatible with both pipeline layouts).
            fixed (DescriptorSet* pSet = &_frameSets[_currentFrame * 2 + preludeParity])
                _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _pipelineLayout,
                    0, 1, pSet, 0, null);

            _vk.CmdPushConstants(cmd, _pipelineLayout, ShaderStageFlags.FragmentBit,
                0, (uint)Unsafe.SizeOf<PushData>(), &pc);

            _vk.CmdDraw(cmd, 3, 1, 0, 0);
            _vk.CmdEndRenderPass(cmd);

            // Transition off-screen image to TRANSFER_SRC for copy/blit
            ImageMemoryBarrier toSrc = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.ColorAttachmentOptimal,
                NewLayout = ImageLayout.TransferSrcOptimal,
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit,
                Image = _offscreenImage,
                SubresourceRange = new ImageSubresourceRange(
                    ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            _vk.CmdPipelineBarrier(
                cmd,
                PipelineStageFlags.ColorAttachmentOutputBit,
                PipelineStageFlags.TransferBit,
                0, 0, null, 0, null, 1, &toSrc
            );

            // Copy off-screen -> staging buffer for CPU readback (if slot available)
            if (canDoStagingCopy)
            {
                BufferImageCopy region = new()
                {
                    BufferOffset = 0,
                    BufferRowLength = 0,
                    BufferImageHeight = 0,
                    ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    ImageOffset = new Offset3D(0, 0, 0),
                    ImageExtent = new Extent3D(_offscreenExtent.Width, _offscreenExtent.Height, 1),
                };
                Buffer dst = _stagingRing.CurrentWriteBuffer;
                _vk.CmdCopyImageToBuffer(cmd, _offscreenImage, ImageLayout.TransferSrcOptimal, dst, 1, &region);
            }

            // DirectComposition mode: blit to shared image for D3D11 presentation
#if WINDOWS
            bool presentDcomp = PRESENT_DCOMP && !_resizePending && !_presentInProgress && _vulkanExternalMemory != null;
            if (presentDcomp)
            {
                // Use actual Vulkan shared image dimensions for the blit destination
                var dstWidth = _vulkanExternalMemory!.Width;
                var dstHeight = _vulkanExternalMemory.Height;

                // Debug: log dimensions being used
                if (!_loggedDcompBlitSize)
                {
                    _loggedDcompBlitSize = true;
                    _logger.LogDebug("[GraphicsHost] DComp blit: offscreen {SrcWidth}x{SrcHeight} to shared image {DstWidth}x{DstHeight}.",
                        _offscreenExtent.Width, _offscreenExtent.Height, dstWidth, dstHeight);
                }

                // Blit offscreen image to shared image for D3D11
                _vulkanExternalMemory.TransitionImageLayout(cmd, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);

                ImageBlit dcompBlit = new()
                {
                    SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    SrcOffsets = new ImageBlit.SrcOffsetsBuffer
                    {
                        Element0 = new(0, 0, 0),
                        Element1 = new((int)_offscreenExtent.Width, (int)_offscreenExtent.Height, 1)
                    },
                    DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    DstOffsets = new ImageBlit.DstOffsetsBuffer
                    {
                        Element0 = new(0, 0, 0),
                        Element1 = new((int)dstWidth, (int)dstHeight, 1)
                    }
                };

                _vk.CmdBlitImage(
                    cmd,
                    _offscreenImage, ImageLayout.TransferSrcOptimal,
                    _vulkanExternalMemory.SharedImage, ImageLayout.TransferDstOptimal,
                    1, &dcompBlit, Filter.Linear
                );

                // Transition shared image to General for D3D11 cross-API access
                _vulkanExternalMemory.TransitionImageLayout(cmd, ImageLayout.TransferDstOptimal, ImageLayout.General);
            }
#endif

            // If we're presenting, blit the off-screen image into the swapchain image
            if (present)
            {
                if (!_loggedFirstBlit)
                {
                    _loggedFirstBlit = true;
                    _logger.LogDebug("[GraphicsHost] Blitting to the swapchain at {Width}x{Height}.",
                        _swapExtent.Width, _swapExtent.Height);
                }

                if (_swapExtent.Width == 0 || _swapExtent.Height == 0)
                {
                    // Mid-teardown. Skip the blit, but do not leave the frame: nothing swapchain-
                    // related has been recorded yet, while the staging copy the rig reads has, and
                    // returning here would skip the bookkeeping at the end that publishes it.
                    NotePresentationSuspended("the swapchain extent is zero");
                    present = false;
                }
            }

            if (present)
            {
                // swapchain image: UNDEFINED -> TRANSFER_DST
                ImageMemoryBarrier swapToDst = new()
                {
                    SType = StructureType.ImageMemoryBarrier,
                    OldLayout = ImageLayout.Undefined,
                    NewLayout = ImageLayout.TransferDstOptimal,
                    SrcAccessMask = 0,
                    DstAccessMask = AccessFlags.TransferWriteBit,
                    Image = _swapchainImages[imageIndex],
                    SubresourceRange = new ImageSubresourceRange(
                        ImageAspectFlags.ColorBit, 0, 1, 0, 1)
                };
                _vk.CmdPipelineBarrier(
                    cmd,
                    PipelineStageFlags.TopOfPipeBit,
                    PipelineStageFlags.TransferBit,
                    0, 0, null, 0, null, 1, &swapToDst
                );

                ImageBlit blit = new()
                {
                    SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    SrcOffsets = new ImageBlit.SrcOffsetsBuffer
                    {
                        Element0 = new(0, 0, 0),
                        Element1 = new((int)_offscreenExtent.Width, (int)_offscreenExtent.Height, 1)
                    },
                    DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    DstOffsets = new ImageBlit.DstOffsetsBuffer
                    {
                        Element0 = new(0, 0, 0),
                        Element1 = new((int)_swapExtent.Width, (int)_swapExtent.Height, 1)
                    }
                };

                _vk.CmdBlitImage(
                    cmd,
                    _offscreenImage, ImageLayout.TransferSrcOptimal,
                    _swapchainImages[imageIndex], ImageLayout.TransferDstOptimal,
                    1, &blit, Filter.Linear
                );

                // TRANSFER_DST -> PRESENT_SRC_KHR
                ImageMemoryBarrier toPresent = swapToDst;
                toPresent.OldLayout = ImageLayout.TransferDstOptimal;
                toPresent.NewLayout = ImageLayout.PresentSrcKhr;
                toPresent.SrcAccessMask = AccessFlags.TransferWriteBit;
                toPresent.DstAccessMask = 0;

                _vk.CmdPipelineBarrier(
                    cmd,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.BottomOfPipeBit,
                    0, 0, null, 0, null, 1, &toPresent
                );
            }

                ImageMemoryBarrier backToColor = new()
                {
                    SType = StructureType.ImageMemoryBarrier,
                    OldLayout = ImageLayout.TransferSrcOptimal,
                    NewLayout = ImageLayout.ColorAttachmentOptimal,
                    SrcAccessMask = AccessFlags.TransferReadBit,
                    DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
                    Image = _offscreenImage,
                    SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
                };
                _vk.CmdPipelineBarrier(
                    cmd,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.ColorAttachmentOutputBit,
                    0, 0, null, 0, null, 1, &backToColor
                );

                _vk.EndCommandBuffer(cmd);

            ulong frameValue = (ulong)Interlocked.Increment(ref _timelineValue);

            // Only wait for CPU release if we're actually using a staging slot
            ulong requiredRelease = canDoStagingCopy ? Volatile.Read(ref _slotNeedCpuRelease[slot]) : 0;
            bool needWait = requiredRelease != 0;

            // --- assemble waits in a single consistent order ---
            int waitCount = 0;
            var waitSems = stackalloc Semaphore[2];            // cpuDoneTL, imageAvailable (if present)
            var waitStages = stackalloc PipelineStageFlags[2];
            var waitVals = stackalloc ulong[2];                // MUST be same length as waitSems

            if (needWait)
            {
                waitSems[waitCount] = _cpuDoneTL;             // timeline
                waitStages[waitCount] = PipelineStageFlags.PipelineStageTopOfPipeBit;
                waitVals[waitCount] = requiredRelease;        // <- value for timeline wait
                waitCount++;
            }
            if (present)
            {
                // The acquiring frame's semaphore, not this frame's — see _acquiredFrameIndex. The
                // two differ whenever an image was acquired and presentation then paused.
                waitSems[waitCount] = _imageAvailableSemaphores[_acquiredFrameIndex]; // binary
                waitStages[waitCount] = PipelineStageFlags.PipelineStageTransferBit;
                waitVals[waitCount] = 0;                      // <- dummy, ignored for binary
                waitCount++;
            }

            // --- assemble signals in a single consistent order ---
            int sigCount = 0;
            var sigSems = stackalloc Semaphore[2];               // gpuDoneTL, renderFinished (if present)
            var sigVals = stackalloc ulong[2];

            ulong producedValue = ++_gpuCtr;
            sigSems[sigCount] = _gpuDoneTL;                      // timeline
            sigVals[sigCount] = producedValue;                   // <- timeline value
            sigCount++;

            if (present)
            {
                sigSems[sigCount] = _renderFinishedByImage[imageIndex]; // binary
                sigVals[sigCount] = 0;                          // <- dummy, ignored for binary
                sigCount++;
            }

            TimelineSemaphoreSubmitInfo tl = new()
            {
                SType = StructureType.TimelineSemaphoreSubmitInfo,
                WaitSemaphoreValueCount = (uint)waitCount,
                PWaitSemaphoreValues = waitCount > 0 ? waitVals : null,
                SignalSemaphoreValueCount = (uint)sigCount,
                PSignalSemaphoreValues = sigCount > 0 ? sigVals : null
            };

            var submitInfo = new SubmitInfo(
                waitSemaphoreCount: (uint)waitCount,
                pWaitSemaphores: waitCount > 0 ? waitSems : null,
                pWaitDstStageMask: waitCount > 0 ? waitStages : null,
                commandBufferCount: 1,
                pCommandBuffers: &cmd,
                signalSemaphoreCount: (uint)sigCount,
                pSignalSemaphores: sigSems
            );
            submitInfo.PNext = &tl;


            // Check again before submit - swapchain may have been destroyed during command buffer recording
            if (present && (_recreatingSwapchain || _swapchain.Handle == 0))
            {
                _imageAcquired = false;
                _acquiredFrameIndex = -1;
                return;
            }

            _vk.ResetFences(_device, 1, in _submitFence);

            // Submit
            if (present)
            {
                var renderFinishedForThisImage = _renderFinishedByImage[imageIndex];
                {
                    // The image-available wait is already in submitInfo, against the slot that
                    // acquired the image rather than the slot rendering it.
                    Semaphore* pSignal = &renderFinishedForThisImage;

                    var res = _vk.QueueSubmit(_graphicsQueue, 1, ref submitInfo, _submitFence);
                    if (res != Result.Success)
                    {
                        if (res == Result.ErrorDeviceLost)
                        {
                            ReportDeviceLost("QueueSubmit (present path)");
                        }
                        else
                        {
                            ReportRenderFailure($"QueueSubmit failed: {res}");
                        }
                        return;
                    }

                    // Present waits on renderFinished
                    fixed (SwapchainKHR* pSwap = &_swapchain)
                    {
                        var presentInfo = new PresentInfoKHR(
                            waitSemaphoreCount: 1, pWaitSemaphores: pSignal,
                            swapchainCount: 1, pSwapchains: pSwap,
                            pImageIndices: &imageIndex
                        );
                        var presentStarted = _stopwatch.Elapsed;
                        var pres = _swapchainExtension.QueuePresent(_graphicsQueue, presentInfo);
                        var presentTook = _stopwatch.Elapsed - presentStarted;
                        if (presentTook >= PresentStallThreshold)
                        {
                            NotePresentStall("QueuePresent", presentTook);
                        }

                        if (!_loggedFirstPresent)
                        {
                            _loggedFirstPresent = true;
                            _logger.LogDebug($"[GraphicsHost] First QueuePresent result: {pres}");
                        }
                        _imageAcquired = false;
                        _acquiredFrameIndex = -1;
                        _imageSerial[imageIndex] = producedValue;

                        // SUBOPTIMAL is the steady state here, not a problem: the swapchain is
                        // deliberately not the layer's size, so the compositor scales it and says
                        // so on every present. Only out-of-date needs action.
                        //
                        // Note there is no early return in this block. The staging copy that feeds
                        // the LEDs is already recorded in this frame's command buffer and has been
                        // submitted; bailing out here would skip the bookkeeping below that
                        // publishes it, which froze the rig on the first frame the moment
                        // presentation started reporting SUBOPTIMAL.
                        if (pres == Result.ErrorOutOfDateKhr && !_isClosing)
                        {
#if WINDOWS
                            RecreateWin32Swapchain(_win32SurfaceWidth, _win32SurfaceHeight);
#elif MACCATALYST
                            RebuildSwapchainAtPinnedSize();
#endif
                        }
                    }
                }
            }
            else
            {
                // No swapchain presentation path: just submit the off-screen work
                var res = _vk.QueueSubmit(_graphicsQueue, 1, ref submitInfo, _submitFence);
                if (res != Result.Success)
                {
                    if (res == Result.ErrorDeviceLost)
                    {
                        ReportDeviceLost("QueueSubmit (offscreen path)");
                    }
                    else
                    {
                        ReportRenderFailure($"QueueSubmit failed: {res}");
                    }
                    return;
                }

#if WINDOWS
                // DirectComposition: after Vulkan submit completes, trigger D3D11 presentation
                // Run async so the vsync-locked Present doesn't block the render loop
                // Use timeline semaphore (not the submit fence) to avoid racing with the render loop
                if (presentDcomp && _dcompPresenter != null)
                {
                    _presentInProgress = true;
                    var presenter = _dcompPresenter;
                    DCompPresentAsync(presenter, producedValue);
                }
#endif
            }

            // Only update slot tracking if we actually used a staging slot
            if (canDoStagingCopy)
            {
                Volatile.Write(ref _slotReadyGpuValue[slot], producedValue);

                // and record that this is the value that must be released before next reuse:
                Volatile.Write(ref _slotNeedCpuRelease[slot], producedValue);

                _slotSignaledValue[_stagingRing.WriteIndex] = frameValue;
                _stagingRing.AdvanceWrite();
            }

            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;

            ReportRenderSuccess();
            }
            catch (Exception ex)
            {
                // Was a bare, silent catch. A frame that throws every time then failed invisibly and
                // forever; the throttle is what makes it a diagnosable fault instead of a hang.
                ReportRenderFailure("Frame failed", ex);
            }
        }

        public unsafe RenderedImageHandle<byte> ReadFromMemory()
        {
            // Nothing will ever be produced again on a lost device, and this path waits on a
            // timeline semaphore with no timeout — so bail before it, rather than risk parking the
            // thread that drives the rig. The animation treats Empty as "keep the last frame".
            if (_deviceLost)
            {
                return RenderedImageHandle<byte>.Empty;
            }

            _readReadyEvent.Wait();

            if (_stagingRing == null || _stagingRing.IsDisposed)
            {
                return RenderedImageHandle<byte>.Empty;
            }

            int slot = _stagingRing.ReadIndex;
            ulong produced = Volatile.Read(ref _slotReadyGpuValue[slot]);
            if (produced == 0)
            {
                for (int i = 1; i < StagingRing.RingSize; i++)
                {
                    int s = (slot + i) % StagingRing.RingSize;
                    ulong p = Volatile.Read(ref _slotReadyGpuValue[s]);
                    if (p != 0) { _stagingRing.ReadIndex = s; slot = s; produced = p; break; }
                }
                if (produced == 0)
                    return RenderedImageHandle<byte>.Empty;
            }

            // (1) Wait for GPU to produce this slot
            SemaphoreWaitInfo wi = new() { SType = StructureType.SemaphoreWaitInfo, SemaphoreCount = 1 };
            fixed (Semaphore* pSem = &_gpuDoneTL)
            {
                wi.PSemaphores = pSem;
                wi.PValues = &produced;
                _vk.WaitSemaphores(_device, &wi, ulong.MaxValue);
            }
            Interlocked.Increment(ref _pendingMemoryReads);

            var memory = _stagingRing.CurrentReadMemory;
            var releaseAction = (int slotValue, ulong producedValue) =>
            {
                try
                {
                    // (3) Signal CPU release - only if this value is higher than what we've signaled
                    // Timeline semaphores require monotonically increasing values
                    ulong current, newValue;
                    do
                    {
                        current = Volatile.Read(ref _cpuSignaled);
                        if (producedValue <= current)
                        {
                            // Already signaled past this value, skip signaling
                            break;
                        }
                        newValue = producedValue;
                    } while (Interlocked.CompareExchange(ref _cpuSignaled, newValue, current) != current);

                    if (producedValue > current)
                    {
                        SemaphoreSignalInfo sig = new() { SType = StructureType.SemaphoreSignalInfo, Semaphore = _cpuDoneTL, Value = producedValue };
                        _vk.SignalSemaphore(_device, &sig);
                    }

                    // (4) Clear and advance
                    Volatile.Write(ref _slotReadyGpuValue[slotValue], 0);
                    _stagingRing.AdvanceRead();
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingMemoryReads);
                }
            };
            return new RenderedImageHandle<byte>(memory, releaseAction, slot, produced);
        }

        public RenderedImageHandle<byte> GetRenderedImage()
        {
            return ReadFromMemory();
        }

        private unsafe ShaderModule CreateShaderModule(byte[] code)
        {
            fixed (byte* codePtr = code)
            {
                var createInfo = new ShaderModuleCreateInfo(
                    codeSize: (nuint)code.Length,
                    pCode: (uint*)codePtr
                );

                if (_vk.CreateShaderModule(_device, createInfo, null, out var shaderModule) != Result.Success)
                {
                    throw new Exception("Failed");
                }
                return shaderModule;
            }
        }

        private unsafe void RecreateGraphicsPipeline()
        {
            // Destroy old pipeline(s)
            if (_pipeline.Handle != 0)
            {
                _vk.DestroyPipeline(_device, _pipeline, null);
                _pipeline = default;
            }

            // Destroy the old fragment module and reload for the new shader
            if (_fragShaderModule.Handle != 0)
            {
                _vk.DestroyShaderModule(_device, _fragShaderModule, null);
                _fragShaderModule = default;
            }

            {
                using var stream = OpenShaderFile($"{_shader}.frag.spv");
                using var streamReader = new BinaryReader(stream);
                var bytes = streamReader.ReadBytes(Int32.MaxValue / 2);
                _fragShaderModule = CreateShaderModule(bytes);
            }

            // Rebuild pipeline with _pipelineLayout and _offscreenRenderPass
            var vp = new Viewport(0, 0, _offscreenExtent.Width, _offscreenExtent.Height, 0f, 1f);
            var sc = new Rect2D(new Offset2D(0, 0), _offscreenExtent);
            var vpState = new PipelineViewportStateCreateInfo(viewportCount: 1, pViewports: &vp, scissorCount: 1, pScissors: &sc);

            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo(stage: ShaderStageFlags.VertexBit, module: _vertShaderModule, pName: (byte*)SilkMarshal.StringToPtr("main"));
            stages[1] = new PipelineShaderStageCreateInfo(stage: ShaderStageFlags.FragmentBit, module: _fragShaderModule, pName: (byte*)SilkMarshal.StringToPtr("main"));

            var vi = new PipelineVertexInputStateCreateInfo() { SType = StructureType.PipelineVertexInputStateCreateInfo };
            var ia = new PipelineInputAssemblyStateCreateInfo(topology: PrimitiveTopology.TriangleList);
            var rs = new PipelineRasterizationStateCreateInfo(polygonMode: PolygonMode.Fill, cullMode: CullModeFlags.CullModeNone, frontFace: FrontFace.CounterClockwise, lineWidth: 1f);
            var ms = new PipelineMultisampleStateCreateInfo(rasterizationSamples: SampleCountFlags.Count1Bit);
            var blendAtt = new PipelineColorBlendAttachmentState(blendEnable: false,
                colorWriteMask: ColorComponentFlags.ColorComponentRBit | ColorComponentFlags.ColorComponentGBit |
                                ColorComponentFlags.ColorComponentBBit | ColorComponentFlags.ColorComponentABit);
            var blend = new PipelineColorBlendStateCreateInfo(attachmentCount: 1, pAttachments: &blendAtt);

            var gpCI = new GraphicsPipelineCreateInfo(
                stageCount: 2, pStages: stages,
                pVertexInputState: &vi, pInputAssemblyState: &ia,
                pViewportState: &vpState, pRasterizationState: &rs,
                pMultisampleState: &ms, pDepthStencilState: null,
                pColorBlendState: &blend, pDynamicState: null,
                layout: _pipelineLayout, renderPass: _offscreenRenderPass, subpass: 0
            );

            _vk.CreateGraphicsPipelines(_device, default, 1, gpCI, null, out _pipeline);
            _stagingRing = new StagingRing(_vk, _device, _memProps, _graphicsQueueIndex, _imageSize);
        }

        public void SetShader(string shader)
        {
            if (_deviceLost)
            {
                // Rebuilding a pipeline on a dead device achieves nothing, and this waits on the
                // render thread and a fence to get there.
                _logger.LogWarning("[GraphicsHost] Ignoring SetShader('{Shader}'): the GPU device is lost.", shader);
                return;
            }

            SpinWait.SpinUntil(() => _running == true);

            // 1) Tell render loop to pause submits
            _readReadyEvent.Reset();              // stop new ReadFromMemory users
            _running = false;                     // ask render loop to exit its while-step boundary
            var rt = _renderThread;
            if (rt != null && rt.IsAlive && Thread.CurrentThread != rt)
                rt.Join();                        // wait for render loop to be fully quiescent

            _vk.WaitForFences(_device, 1, in _submitFence, true, ulong.MaxValue);

            // 2) Drain GPU once (no WSI teardown)
            _vk.QueueWaitIdle(_graphicsQueue);

            // Bring _cpuDoneTL up to match _gpuCtr so no stale waits remain
            if (_gpuCtr > _cpuSignaled)
            {
                unsafe
                {
                    SemaphoreSignalInfo sig = new()
                    {
                        SType = StructureType.SemaphoreSignalInfo,
                        Semaphore = _cpuDoneTL,
                        Value = _gpuCtr
                    };
                    _vk.SignalSemaphore(_device, &sig);
                    _cpuSignaled = _gpuCtr;
                }
            }

            // Clear slot tracking to prevent stale values from blocking new staging ring
            for (int i = 0; i < StagingRing.RingSize; i++)
            {
                _slotReadyGpuValue[i] = 0;
                _slotNeedCpuRelease[i] = 0;
            }

            _stagingRing?.Dispose();
            _stagingRing = null;

            // 3) Rebuild pipeline with the new fragment shader
            _shader = shader;
            RecreateGraphicsPipeline();           // destroy old _pipeline + chosen frag module, create new

            // 4) Resume rendering
            _running = true;
            _renderThread = new Thread(RenderLoop) { IsBackground = true };
            _renderThread.Start();
            _readReadyEvent.Set();
        }
    }
}