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

            // Wire up events from the Metal surface view
            metalSurface.LayerReady += OnLayerReady;
            metalSurface.DrawableSizeChanged += OnDrawableSizeChanged;
            metalSurface.ViewRemoving += OnViewRemoving;
        }

        private void OnLayerReady(object? sender, nint layerPtr)
        {
            System.Diagnostics.Debug.WriteLine("[PreviewPage] Metal layer ready");
            _graphicsHost.EnableMetalPresentation(layerPtr, (uint)metalSurface.Width, (uint)metalSurface.Height);
        }

        private void OnDrawableSizeChanged(object? sender, DrawableSizeChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[PreviewPage] Drawable size changed: {e.Width}x{e.Height}");
            _graphicsHost.HandleMetalSurfaceResize((uint)e.Width, (uint)e.Height);
        }

        private void OnViewRemoving(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[PreviewPage] View removing");
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            System.Diagnostics.Debug.WriteLine("[PreviewPage] Disappearing");
            _graphicsHost.OnPreviewWindowClosed();
        }
    }
}
