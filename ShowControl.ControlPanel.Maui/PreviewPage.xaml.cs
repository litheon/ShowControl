using ShowControl.Graphics;
#if WINDOWS
using ShowControl.ControlPanel.Maui.Platforms.Windows;
#endif

namespace ShowControl.ControlPanel.Maui
{
    public partial class PreviewPage : ContentPage
    {
        private readonly IGraphicsHost _graphicsHost;
#if WINDOWS
        private bool _useDirectComposition = true; // Enable DirectComposition by default for XAML overlay support
#endif

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
            if (_useDirectComposition && vulkanSurface.Handler is SwapChainPanelHandler swapChainHandler)
            {
                // DirectComposition mode: use Vulkan/D3D11 interop for XAML overlay support
                var d3d11Host = swapChainHandler.D3D11Host;
                if (d3d11Host != null)
                {
                    // Use D3D11Host's swap chain dimensions to ensure shared texture matches
                    var width = d3d11Host.Width;
                    var height = d3d11Host.Height;
                    System.Diagnostics.Debug.WriteLine($"[PreviewPage] Using DirectComposition mode at {width}x{height} (from D3D11Host)");
                    _graphicsHost.EnableDirectCompositionPresentation(d3d11Host, width, height);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[PreviewPage] D3D11Host is null, falling back to Win32 mode");
                    _graphicsHost.EnableWin32Presentation(surfaceHandle, (uint)vulkanSurface.Width, (uint)vulkanSurface.Height);
                }
            }
            else
            {
                // Win32 mode: direct Vulkan presentation (no XAML overlay support)
                System.Diagnostics.Debug.WriteLine("[PreviewPage] Using Win32 mode");
                _graphicsHost.EnableWin32Presentation(surfaceHandle, (uint)vulkanSurface.Width, (uint)vulkanSurface.Height);
            }
#endif
        }

        private void OnSurfaceSizeChanged(object? sender, SurfaceSizeChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[PreviewPage] Surface size changed: {e.Width}x{e.Height}");
#if MACCATALYST
            _graphicsHost.HandleMetalSurfaceResize((uint)e.Width, (uint)e.Height);
#elif WINDOWS
            if (_useDirectComposition && vulkanSurface.Handler is SwapChainPanelHandler)
            {
                _graphicsHost.HandleDirectCompositionResize((uint)e.Width, (uint)e.Height);
            }
            else
            {
                _graphicsHost.HandleWin32SurfaceResize((uint)e.Width, (uint)e.Height);
            }
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
