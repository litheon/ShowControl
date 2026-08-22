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

        /// <summary>
        /// Fixes the native drawable at this size in pixels instead of following the view's bounds.
        /// Zero means follow the bounds, which is the ordinary behaviour for a renderer that draws at
        /// the window's resolution.
        /// <para>
        /// This one does not: it renders a fixed offscreen image and blits that to the surface, so a
        /// drawable that tracked the window bought only the sharpness of the final upscale — and cost
        /// a swapchain rebuild on every resize step, which is a heavyweight GPU operation on a path
        /// driven by a mouse drag. Pinned, the swapchain is created once and never goes out of date;
        /// the compositor scales the drawable to the layer instead.
        /// </para>
        /// </summary>
        public int PinnedDrawableWidth { get; set; }

        public int PinnedDrawableHeight { get; set; }

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
