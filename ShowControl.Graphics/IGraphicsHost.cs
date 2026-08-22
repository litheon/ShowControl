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

    public interface IGraphicsHost
    {
        public RenderedImageHandle<byte> GetRenderedImage();
        public int Width { get;  }
        public int Height { get; }
        public System.Drawing.Color[] Colors { get; set; }
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
