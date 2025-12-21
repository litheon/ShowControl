using ShowControl.Graphics;

namespace ShowControl.ControlPanel.Maui
{
    public partial class PreviewPage : ContentPage
    {
        private readonly IGraphicsHost _graphicsHost;

        public PreviewPage(IGraphicsHost graphicsHost)
        {
            InitializeComponent();
            _graphicsHost = graphicsHost;

            // Wire up events from the Vulkan surface view
            vulkanSurface.SurfaceReady += OnSurfaceReady;
            vulkanSurface.SurfaceSizeChanged += OnSurfaceSizeChanged;
            vulkanSurface.SurfaceDestroying += OnSurfaceDestroying;
        }

        private void OnSurfaceReady(object? sender, nint surfaceHandle)
        {
            System.Diagnostics.Debug.WriteLine("[PreviewPage] Surface ready");
#if MACCATALYST
            _graphicsHost.EnableMetalPresentation(surfaceHandle, (uint)vulkanSurface.Width, (uint)vulkanSurface.Height);
#elif WINDOWS
            _graphicsHost.EnableWin32Presentation(surfaceHandle, (uint)vulkanSurface.Width, (uint)vulkanSurface.Height);
#endif
        }

        private void OnSurfaceSizeChanged(object? sender, SurfaceSizeChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[PreviewPage] Surface size changed: {e.Width}x{e.Height}");
#if MACCATALYST
            _graphicsHost.HandleMetalSurfaceResize((uint)e.Width, (uint)e.Height);
#elif WINDOWS
            _graphicsHost.HandleWin32SurfaceResize((uint)e.Width, (uint)e.Height);
#endif
        }

        private void OnSurfaceDestroying(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[PreviewPage] Surface destroying");
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            System.Diagnostics.Debug.WriteLine("[PreviewPage] Disappearing");
            _graphicsHost.OnPreviewWindowClosed();
        }
    }
}
