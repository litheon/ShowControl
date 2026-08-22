using CoreAnimation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using ObjCRuntime;
using ShowControl.Graphics;
using UIKit;

namespace ShowControl.ControlPanel.Maui.Platforms.MacCatalyst
{
    /// <summary>
    /// Platform handler for MetalSurfaceView on Mac Catalyst.
    /// Creates a UIView backed by CAMetalLayer for Vulkan/Metal rendering.
    /// </summary>
    public partial class MetalSurfaceHandler : ViewHandler<MetalSurfaceView, MetalBackedView>
    {
        public static IPropertyMapper<MetalSurfaceView, MetalSurfaceHandler> Mapper =
            new PropertyMapper<MetalSurfaceView, MetalSurfaceHandler>(ViewMapper);

        public MetalSurfaceHandler() : base(Mapper)
        {
        }

        protected override MetalBackedView CreatePlatformView()
        {
            var view = new MetalBackedView();
            return view;
        }

        protected override void ConnectHandler(MetalBackedView platformView)
        {
            base.ConnectHandler(platformView);

            platformView.LayerReady += OnLayerReady;
            platformView.SizeChanged += OnSizeChanged;
            platformView.ViewRemoving += OnViewRemoving;
        }

        protected override void DisconnectHandler(MetalBackedView platformView)
        {
            platformView.LayerReady -= OnLayerReady;
            platformView.SizeChanged -= OnSizeChanged;
            platformView.ViewRemoving -= OnViewRemoving;

            base.DisconnectHandler(platformView);
        }

        private void OnLayerReady(object? sender, nint layerPtr)
        {
            VirtualView?.OnLayerReady(layerPtr);
        }

        private void OnSizeChanged(object? sender, CGSize size)
        {
            VirtualView?.OnDrawableSizeChanged(size.Width, size.Height);
        }

        private void OnViewRemoving(object? sender, EventArgs e)
        {
            VirtualView?.OnViewRemoving();
        }
    }

    /// <summary>
    /// Platform handler for VulkanSurfaceView on Mac Catalyst.
    /// Uses the same MetalBackedView as MetalSurfaceHandler.
    /// </summary>
    public partial class VulkanSurfaceHandler : ViewHandler<VulkanSurfaceView, MetalBackedView>
    {
        public static IPropertyMapper<VulkanSurfaceView, VulkanSurfaceHandler> Mapper =
            new PropertyMapper<VulkanSurfaceView, VulkanSurfaceHandler>(ViewMapper);

        public VulkanSurfaceHandler() : base(Mapper)
        {
        }

        protected override MetalBackedView CreatePlatformView()
        {
            return new MetalBackedView();
        }

        protected override void ConnectHandler(MetalBackedView platformView)
        {
            base.ConnectHandler(platformView);

            // Read before the first layout pass, so the drawable is created at the pinned size
            // rather than at the window's and then changed to it.
            platformView.PinnedDrawableSize =
                VirtualView is { PinnedDrawableWidth: > 0, PinnedDrawableHeight: > 0 }
                    ? new CGSize(VirtualView.PinnedDrawableWidth, VirtualView.PinnedDrawableHeight)
                    : null;

            platformView.LayerReady += OnLayerReady;
            platformView.SizeChanged += OnSizeChanged;
            platformView.ViewRemoving += OnViewRemoving;
        }

        protected override void DisconnectHandler(MetalBackedView platformView)
        {
            platformView.LayerReady -= OnLayerReady;
            platformView.SizeChanged -= OnSizeChanged;
            platformView.ViewRemoving -= OnViewRemoving;

            base.DisconnectHandler(platformView);
        }

        private void OnLayerReady(object? sender, nint layerPtr)
        {
            VirtualView?.OnSurfaceReady(layerPtr);
        }

        private void OnSizeChanged(object? sender, CGSize size)
        {
            VirtualView?.OnSurfaceSizeChanged(size.Width, size.Height);
        }

        private void OnViewRemoving(object? sender, EventArgs e)
        {
            VirtualView?.OnSurfaceDestroying();
        }
    }

    /// <summary>
    /// UIView subclass with CAMetalLayer as its backing layer.
    /// This provides the surface for Vulkan/MoltenVK rendering.
    /// </summary>
    public class MetalBackedView : UIView
    {
        public event EventHandler<nint>? LayerReady;
        public event EventHandler<CGSize>? SizeChanged;
        public event EventHandler? ViewRemoving;

        private bool _layerReported = false;

        public MetalBackedView() : base()
        {
            // Ensure the view uses the full area and handles display scaling
            ContentScaleFactor = UIScreen.MainScreen.Scale;
            BackgroundColor = UIColor.Black;
        }

        /// <summary>
        /// Override LayerClass to make CAMetalLayer the backing layer.
        /// </summary>
        [Export("layerClass")]
        public static Class LayerClass()
        {
            return new Class(typeof(CAMetalLayer));
        }

        /// <summary>
        /// Gets the CAMetalLayer backing this view.
        /// </summary>
        public CAMetalLayer MetalLayer => (CAMetalLayer)Layer;

        /// <summary>
        /// Fixed drawable size in pixels, or null to follow the view's bounds.
        /// <para>
        /// Pinned is what this app wants: it renders a fixed offscreen image and blits that here, so
        /// a drawable tracking the window only moved the final upscale from the compositor into the
        /// blit — and made every resize step rebuild the swapchain. Pinned, the swapchain never goes
        /// out of date and Core Animation scales the drawable to the layer instead.
        /// </para>
        /// </summary>
        public CGSize? PinnedDrawableSize { get; set; }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();

            var newSize = PinnedDrawableSize ?? new CGSize(
                Bounds.Width * ContentScaleFactor,
                Bounds.Height * ContentScaleFactor
            );

            if (newSize.Width <= 0 || newSize.Height <= 0)
            {
                return;
            }

            // Only when it actually changes. Pinned, that is once — so the resize path downstream
            // never runs, which is the point of pinning.
            if (MetalLayer.DrawableSize != newSize)
            {
                MetalLayer.DrawableSize = newSize;
                SizeChanged?.Invoke(this, newSize);
            }

            // Report layer ready on first valid size if not already reported
            if (!_layerReported)
            {
                _layerReported = true;
                LayerReady?.Invoke(this, MetalLayer.Handle);
            }
        }

        public override void MovedToWindow()
        {
            base.MovedToWindow();

            if (Window != null)
            {
                // View is being added to a window
                if (!_layerReported && Bounds.Width > 0 && Bounds.Height > 0)
                {
                    _layerReported = true;
                    MetalLayer.DrawableSize = PinnedDrawableSize ?? new CGSize(
                        Bounds.Width * ContentScaleFactor,
                        Bounds.Height * ContentScaleFactor
                    );
                    LayerReady?.Invoke(this, MetalLayer.Handle);
                }
            }
            else
            {
                // View is being removed from window
                ViewRemoving?.Invoke(this, EventArgs.Empty);
                _layerReported = false;
            }
        }
    }
}
