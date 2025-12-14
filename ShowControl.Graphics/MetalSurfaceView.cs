using Microsoft.Maui.Controls;
using System;

namespace ShowControl.Graphics
{
    /// <summary>
    /// Event args for when GraphicsHost requests a preview window.
    /// </summary>
    public class PreviewWindowRequestedEventArgs : EventArgs
    {
        public uint Width { get; }
        public uint Height { get; }

        public PreviewWindowRequestedEventArgs(uint width, uint height)
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Custom MAUI View that hosts a Metal surface (CAMetalLayer on Mac Catalyst).
    /// Used for displaying Vulkan graphics output via MoltenVK.
    /// </summary>
    public class MetalSurfaceView : View
    {
        /// <summary>
        /// Fired when the underlying CAMetalLayer is ready and available.
        /// The nint parameter is the pointer to the CAMetalLayer.
        /// </summary>
        public event EventHandler<nint>? LayerReady;

        /// <summary>
        /// Fired when the drawable size changes (e.g., on resize).
        /// </summary>
        public event EventHandler<DrawableSizeChangedEventArgs>? DrawableSizeChanged;

        /// <summary>
        /// Fired when the view is being removed from window (cleanup signal).
        /// </summary>
        public event EventHandler? ViewRemoving;

        public void OnLayerReady(nint layerPtr)
        {
            LayerReady?.Invoke(this, layerPtr);
        }

        public void OnDrawableSizeChanged(double width, double height)
        {
            DrawableSizeChanged?.Invoke(this, new DrawableSizeChangedEventArgs(width, height));
        }

        public void OnViewRemoving()
        {
            ViewRemoving?.Invoke(this, EventArgs.Empty);
        }
    }

    public class DrawableSizeChangedEventArgs : EventArgs
    {
        public double Width { get; }
        public double Height { get; }

        public DrawableSizeChangedEventArgs(double width, double height)
        {
            Width = width;
            Height = height;
        }
    }
}
