using ShowControl.Graphics;
#if WINDOWS
using ShowControl.ControlPanel.Maui.Platforms.Windows;
#endif

namespace ShowControl.ControlPanel.Maui
{
    public partial class PreviewPage : ContentPage
    {
        private readonly IGraphicsHost _graphicsHost;
        private IDispatcherTimer? _paramTimer;
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

            _paramTimer = Dispatcher.CreateTimer();
            _paramTimer.Interval = TimeSpan.FromMilliseconds(250);
            _paramTimer.Tick += (s, e) => UpdateShaderParamsLabel();
            _paramTimer.Start();
        }

        private void UpdateShaderParamsLabel()
        {
            var shader = _graphicsHost.CurrentShader;
            string p1Label, p2Label, p3Label, p4Label;

            if (shader == Shaders.Kaleidoscope)
            {
                p1Label = "Spiral Twist";
                p2Label = "Time Offset";
                p3Label = "Ring Density";
                p4Label = "Flow Freq";
            }
            else if (shader == Shaders.Clown)
            {
                p1Label = "Blades";
                p2Label = "Spin Offset";
                p3Label = "Hue Offset";
                p4Label = "Dot Radius";
            }
            else
            {
                p1Label = "Param1";
                p2Label = "Param2";
                p3Label = "Param3";
                p4Label = "Param4";
            }

            ShaderParamsLabel.Text = $"Shader: {shader}\n" +
                $"Distortion: {_graphicsHost.DistortionStrength:F3}\n" +
                $"{p1Label}: {_graphicsHost.ShaderParam1:F3}\n" +
                $"{p2Label}: {_graphicsHost.ShaderParam2:F3}\n" +
                $"{p3Label}: {_graphicsHost.ShaderParam3:F3}\n" +
                $"{p4Label}: {_graphicsHost.ShaderParam4:F3}";
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
            _paramTimer?.Stop();
            System.Diagnostics.Debug.WriteLine("[PreviewPage] Disappearing");
            _graphicsHost.OnPreviewWindowClosed();
        }
    }
}
