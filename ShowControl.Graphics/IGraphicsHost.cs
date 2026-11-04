using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Graphics
{
    public static class Shaders
    {
        public const string Kaleidoscope = "kaleidoscope";
        public const string Geometry = "geometry";
        public const string Clown = "clown";
        public const string AudioLite = "audiolite";

        /// <summary>Two-stage fluid flow-field visualization: a compute prelude
        /// (fluid.comp) simulates a spectrum-fed field, fluid.frag renders it.</summary>
        public const string Fluid = "fluid";
    }

    /// <summary>
    /// The 8-float AudioLite contract as the graphics host receives it: appended to the
    /// push-constant block, so every fragment shader may read it (or declare only the
    /// prefix and ignore it). Append-only — new fields go on the end, never reorder.
    /// Values arrive already bounded and NaN-free from the analysis side; Graphics does
    /// not reference the analysis project (the Graphics ↛ PixelMapping precedent), which
    /// is why this mirror type exists.
    /// </summary>
    public readonly record struct AudioLitePush(
        float Energy,
        float Impulse,
        float Tension,
        float Phase,
        float Hue,
        float Grain,
        float Motion,
        float Spread)
    {
        /// <summary>What shaders see before the first publish or with audio off.</summary>
        public static readonly AudioLitePush Silence = new(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0.5f);
    }
    
    public enum Shader
    {
        Kaleidoscope, Geometry
    }

    /// <summary>
    /// Callback interface for DirectComposition presentation.
    /// D3D11 creates and owns the shared texture, Vulkan imports it.
    /// </summary>
    public interface IDirectCompositionPresenter
    {
        /// <summary>
        /// Creates a shared texture that Vulkan can import.
        /// Returns the NT handle for Vulkan to import.
        /// </summary>
        nint CreateSharedTexture(uint width, uint height);

        /// <summary>
        /// Gets the current shared texture handle.
        /// </summary>
        nint SharedTextureHandle { get; }

        /// <summary>
        /// Gets the current width of the swap chain/shared texture.
        /// </summary>
        uint Width { get; }

        /// <summary>
        /// Gets the current height of the swap chain/shared texture.
        /// </summary>
        uint Height { get; }

        /// <summary>
        /// Copies the shared texture to the SwapChain and presents.
        /// </summary>
        /// <param name="acquireKey">Mutex key to acquire (Vulkan has released this)</param>
        /// <param name="releaseKey">Mutex key to release (for Vulkan to acquire)</param>
        void CopyAndPresent(ulong acquireKey, ulong releaseKey);

        /// <summary>
        /// Resizes the SwapChain and recreates the shared texture.
        /// Returns the new shared texture handle.
        /// </summary>
        nint Resize(uint width, uint height);

        /// <summary>
        /// Indicates if the swap chain was recreated during the last resize.
        /// If true, the caller should rebind the swap chain to the panel.
        /// </summary>
        bool SwapChainRecreated { get; }

        /// <summary>
        /// Event raised when the swap chain is recreated and needs to be rebound to the panel.
        /// </summary>
        event Action? SwapChainRecreatedEvent;
    }

    /// <summary>
    /// Which of the fluid's injection discs a control-surface pad belongs to. Every pad
    /// has its own disc, so pressing another pad floods another point; with more pads
    /// than discs they share round-robin. The analysis floods by this (RenderService →
    /// AudioAnalysisEngine) and the host colours by it (ShaderAnimation → GraphicsHost),
    /// so the one function keeps the two in step.
    /// </summary>
    public static class InjectionDiscs
    {
        public const int Count = 5;   // GraphicsHost.InjectionSites, InjectionHistory.BandCount

        public static int Of(int pad) => ((pad % Count) + Count) % Count;
    }

    /// <summary>A held pad's colour on the injection disc it belongs to.</summary>
    public readonly record struct PadColor(int Disc, System.Drawing.Color Color);

    public interface IGraphicsHost
    {
        public RenderedImageHandle<byte> GetRenderedImage();
        public int Width { get;  }
        public int Height { get; }
        /// <summary>The colours of the pads held on the control surface, for the
        /// sector/ring regions the shaders paint in them — drawn only while
        /// <see cref="ColorRegions"/> is on.</summary>
        public System.Drawing.Color[] Colors { get; set; }

        /// <summary>The held pads' colours by injection disc (<see cref="InjectionDiscs"/>):
        /// while a pad is down its disc injects in its colour, and the flood a press fires
        /// through the analysis comes out of that disc in that colour. A disc not listed
        /// injects the analysis palette's hue. Replaced whole on every press and release;
        /// read once per frame.</summary>
        public PadColor[] PadColors { get; set; }

        /// <summary>Whether <see cref="Colors"/> reach the shaders' colour buffer, i.e.
        /// whether the pad-colour regions (the kaleidoscope's spiral bias, the fluid's
        /// overlay) are drawn. Off, the shaders see no held colours; the injection still does.</summary>
        public bool ColorRegions { get; set; }
        public void SetShader(string fragmentShaderResource);

        /// <summary>Global distortion strength driven by rotary 1.</summary>
        public float DistortionStrength { get; set; }

        /// <summary>Shader-specific knob parameters driven by rotary encoders 2–5.</summary>
        public float ShaderParam1 { get; set; }
        public float ShaderParam2 { get; set; }
        public float ShaderParam3 { get; set; }
        public float ShaderParam4 { get; set; }

        /// <summary>Name of the currently active shader.</summary>
        public string CurrentShader { get; }

        /// <summary>
        /// Publishes the current AudioLite frame for the render loop to fold into the next
        /// frame's push constants. Callable from any thread at any rate (the analysis engine
        /// publishes at ~94Hz); the render thread reads the latest value once per frame.
        /// Independent of the active shader — audio state flows whatever is on screen.
        /// </summary>
        void SetAudio(in AudioLitePush audio);

        /// <summary>
        /// Publishes the current FFT spectrum (magnitudes, DC first; 1024 bins expected,
        /// longer input is truncated and shorter is zero-padded). Callable from any thread;
        /// latest-wins, copied immediately — the caller's span is not retained. The render
        /// thread uploads the newest spectrum once per frame for shaders that declare the
        /// spectrum storage buffer (binding 1).
        /// <paramref name="injectionTable"/> rides in the same buffer after the bins: for
        /// each of 5 log-spaced bands of the spectrum, where in the plane that band
        /// currently enters and in what color — 5 positions across, 5 depths, then 5
        /// hues, each 0..1 — then how hard each is injecting dye (5 pulses, 0..1: a
        /// constant baseline, over which the occasional hit at the loudest of the last
        /// few minutes fires a one-shot envelope to 1) —
        /// followed by the field's sideways bias as two speeds, of
        /// which at most one is nonzero: the drift of all the fluid (+ right, − left,
        /// plane widths per second), and the injection points' left/right emission —
        /// then how marbled the dye is shown, 0..1, how firmly the control surface's
        /// pads hold the colours (the host's own slot), and last how hard the hits are
        /// stirring the flow, 0..1 (see InjectionHistory, InjectionPalette,
        /// InjectionPulses and FlowBias in ShowControl.AudioAnalysis).
        /// Any other length falls back to plain frequency order along the top, one hue
        /// per band, nothing injecting, no bias, fully marbled, nothing stirring.
        /// </summary>
        void SetAudioSpectrum(ReadOnlySpan<float> magnitudes, ReadOnlySpan<float> injectionTable);

        /// <summary>
        /// Publishes the drop signal for shaders: positive = seconds until the nearest
        /// announced or waveform-predicted drop, negative = seconds since one just fired
        /// (a short post-hit window), and ≥900 = no drop known. Callable from any thread;
        /// latest-wins. Uploaded in the spectrum buffer's header once per frame.
        /// </summary>
        void SetDropSignal(float signedSeconds);

        /// <summary>
        /// Event raised when the graphics host wants a preview window to be opened.
        /// The MAUI app should handle this event and create the window.
        /// </summary>
        event EventHandler<PreviewWindowRequestedEventArgs>? PreviewWindowRequested;

        /// <summary>
        /// Called by the MAUI app when the Metal layer is ready (Mac Catalyst).
        /// </summary>
        void EnableMetalPresentation(nint metalLayerPtr, uint width, uint height);

        /// <summary>
        /// Called by the MAUI app when the Win32 HWND is ready (Windows).
        /// </summary>
        void EnableWin32Presentation(nint hwnd, uint width, uint height);

        /// <summary>
        /// Called by the MAUI app when the DirectComposition SwapChain is ready (Windows).
        /// Uses Vulkan/D3D11 interop for XAML overlay support.
        /// </summary>
        void EnableDirectCompositionPresentation(IDirectCompositionPresenter presenter, uint width, uint height);

        /// <summary>
        /// Called by the MAUI app when the Metal surface is resized.
        /// </summary>
        void HandleMetalSurfaceResize(uint width, uint height);

        /// <summary>
        /// Called by the MAUI app when the Win32 surface is resized.
        /// </summary>
        void HandleWin32SurfaceResize(uint width, uint height);

        /// <summary>
        /// Called by the MAUI app when the DirectComposition surface is resized.
        /// </summary>
        void HandleDirectCompositionResize(uint width, uint height);

        /// <summary>
        /// Called by the MAUI app when the preview window is closed.
        /// </summary>
        void OnPreviewWindowClosed();

        /// <summary>
        /// Turns presenting on and off as the preview window becomes visible or hidden.
        /// <para>
        /// Not an optimisation. A frame submitted for presentation waits on the swapchain's
        /// image-available semaphore, and that submission also carries the copy the LEDs are fed
        /// from — so while a minimised window withholds its drawable, the submission never executes
        /// and the rig stops with it. Disabled, the frame carries no swapchain work at all and the
        /// pixels keep flowing.
        /// </para>
        /// </summary>
        void SetPresentationEnabled(bool enabled);
    }
}
