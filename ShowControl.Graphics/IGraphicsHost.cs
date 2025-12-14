using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Graphics
{
    public enum Shader { Kaleidoscope, Geometry};
    public interface IGraphicsHost
    {
        public RenderedImageHandle<byte> GetRenderedImage();
        public int Width { get;  }
        public int Height { get; }
        public System.Drawing.Color[] Colors { get; set; }
        public void SetShader(Shader shader);

        /// <summary>
        /// Event raised when the graphics host wants a preview window to be opened.
        /// The MAUI app should handle this event and create the window.
        /// </summary>
        event EventHandler<PreviewWindowRequestedEventArgs>? PreviewWindowRequested;

        /// <summary>
        /// Called by the MAUI app when the Metal layer is ready.
        /// </summary>
        void EnableMetalPresentation(nint metalLayerPtr, uint width, uint height);

        /// <summary>
        /// Called by the MAUI app when the Metal surface is resized.
        /// </summary>
        void HandleMetalSurfaceResize(uint width, uint height);

        /// <summary>
        /// Called by the MAUI app when the preview window is closed.
        /// </summary>
        void OnPreviewWindowClosed();
    }
}
