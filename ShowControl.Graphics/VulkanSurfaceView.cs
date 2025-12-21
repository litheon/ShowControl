using Microsoft.Maui.Controls;
using System;

namespace ShowControl.Graphics
{
    /// <summary>
    /// Cross-platform MAUI View that hosts a Vulkan rendering surface.
    /// On Mac Catalyst: Uses CAMetalLayer via MoltenVK
    /// On Windows: Uses Win32 surface via VK_KHR_win32_surface
    /// </summary>
    public class VulkanSurfaceView : View
    {
        /// <summary>
        /// Fired when the underlying native surface is ready.
        /// On Mac: nint is the CAMetalLayer pointer
        /// On Windows: nint is the HWND
        /// </summary>
        public event EventHandler<nint>? SurfaceReady;

        /// <summary>
        /// Fired when the surface size changes (e.g., on resize).
        /// </summary>
        public event EventHandler<SurfaceSizeChangedEventArgs>? SurfaceSizeChanged;

        /// <summary>
        /// Fired when the view is being removed from window (cleanup signal).
        /// </summary>
        public event EventHandler? SurfaceDestroying;

        public void OnSurfaceReady(nint surfaceHandle)
        {
            SurfaceReady?.Invoke(this, surfaceHandle);
        }

        public void OnSurfaceSizeChanged(double width, double height)
        {
            SurfaceSizeChanged?.Invoke(this, new SurfaceSizeChangedEventArgs(width, height));
        }

        public void OnSurfaceDestroying()
        {
            SurfaceDestroying?.Invoke(this, EventArgs.Empty);
        }
    }

    public class SurfaceSizeChangedEventArgs : EventArgs
    {
        public double Width { get; }
        public double Height { get; }

        public SurfaceSizeChangedEventArgs(double width, double height)
        {
            Width = width;
            Height = height;
        }
    }
}
