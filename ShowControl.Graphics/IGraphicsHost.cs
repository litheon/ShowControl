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

    public interface IGraphicsHost
    {
        public RenderedImageHandle<byte> GetRenderedImage();
        public int Width { get;  }
        public int Height { get; }
        public System.Drawing.Color[] Colors { get; set; }
        public void SetShader(string fragmentShaderResource);

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
        /// Called by the MAUI app when the Metal surface is resized.
        /// </summary>
        void HandleMetalSurfaceResize(uint width, uint height);

        /// <summary>
        /// Called by the MAUI app when the Win32 surface is resized.
        /// </summary>
        void HandleWin32SurfaceResize(uint width, uint height);

        /// <summary>
        /// Called by the MAUI app when the preview window is closed.
        /// </summary>
        void OnPreviewWindowClosed();
    }
}
