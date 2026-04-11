using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShowControl.Graphics;
using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace ShowControl.ControlPanel.Maui.Platforms.Windows
{
    /// <summary>
    /// Platform handler for VulkanSurfaceView on Windows.
    /// Creates a panel that provides an HWND for Vulkan rendering.
    /// </summary>
    public partial class Win32SurfaceHandler : ViewHandler<VulkanSurfaceView, Win32SurfacePanel>
    {
        public static IPropertyMapper<VulkanSurfaceView, Win32SurfaceHandler> Mapper =
            new PropertyMapper<VulkanSurfaceView, Win32SurfaceHandler>(ViewMapper);

        public Win32SurfaceHandler() : base(Mapper)
        {
        }

        protected override Win32SurfacePanel CreatePlatformView()
        {
            return new Win32SurfacePanel();
        }

        protected override void ConnectHandler(Win32SurfacePanel platformView)
        {
            base.ConnectHandler(platformView);

            platformView.HwndReady += OnHwndReady;
            platformView.SizeChanged += OnSizeChanged;
            platformView.Unloaded += OnUnloaded;
        }

        protected override void DisconnectHandler(Win32SurfacePanel platformView)
        {
            platformView.HwndReady -= OnHwndReady;
            platformView.SizeChanged -= OnSizeChanged;
            platformView.Unloaded -= OnUnloaded;

            base.DisconnectHandler(platformView);
        }

        private void OnHwndReady(object? sender, nint hwnd)
        {
            VirtualView?.OnSurfaceReady(hwnd);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            var scale = GetScaleFactor();
            VirtualView?.OnSurfaceSizeChanged(
                e.NewSize.Width * scale,
                e.NewSize.Height * scale);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            VirtualView?.OnSurfaceDestroying();
        }

        private double GetScaleFactor()
        {
            if (PlatformView?.XamlRoot != null)
            {
                return PlatformView.XamlRoot.RasterizationScale;
            }
            return 1.0;
        }
    }

    /// <summary>
    /// WinUI3 panel that provides an HWND for Vulkan rendering.
    /// Uses a child window for the rendering surface.
    /// </summary>
    public class Win32SurfacePanel : Panel
    {
        public event EventHandler<nint>? HwndReady;

        private nint _childHwnd = IntPtr.Zero;
        private bool _hwndReported = false;

        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint CreateWindowExW(
            int dwExStyle,
            [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
            [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
            int dwStyle,
            int x, int y, int nWidth, int nHeight,
            nint hWndParent,
            nint hMenu,
            nint hInstance,
            nint lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveWindow(nint hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("kernel32.dll")]
        private static extern nint GetModuleHandleW(nint lpModuleName);

        public Win32SurfacePanel()
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black);
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CreateChildWindow();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DestroyChildWindow();
        }

        private void CreateChildWindow()
        {
            if (_childHwnd != IntPtr.Zero) return;

            // Get the parent window's HWND
            var xamlWindow = GetParentWindow();
            if (xamlWindow == null) return;

            nint parentHwnd = WindowNative.GetWindowHandle(xamlWindow);
            if (parentHwnd == IntPtr.Zero) return;

            // Get position and size in pixels
            var transform = TransformToVisual(null);
            var position = transform.TransformPoint(new global::Windows.Foundation.Point(0, 0));
            var scale = XamlRoot?.RasterizationScale ?? 1.0;

            int x = (int)(position.X * scale);
            int y = (int)(position.Y * scale);
            int width = (int)(ActualWidth * scale);
            int height = (int)(ActualHeight * scale);

            if (width <= 0 || height <= 0) return;

            // Create child window for Vulkan surface
            _childHwnd = CreateWindowExW(
                0,
                "Static",  // Simple window class
                "VulkanSurface",
                WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                x, y, width, height,
                parentHwnd,
                IntPtr.Zero,
                GetModuleHandleW(IntPtr.Zero),
                IntPtr.Zero);

            if (_childHwnd != IntPtr.Zero && !_hwndReported)
            {
                _hwndReported = true;
                HwndReady?.Invoke(this, _childHwnd);
            }
        }

        private void DestroyChildWindow()
        {
            if (_childHwnd != IntPtr.Zero)
            {
                DestroyWindow(_childHwnd);
                _childHwnd = IntPtr.Zero;
                _hwndReported = false;
            }
        }

        protected override global::Windows.Foundation.Size ArrangeOverride(global::Windows.Foundation.Size finalSize)
        {
            var result = base.ArrangeOverride(finalSize);

            // Update child window position and size
            if (_childHwnd != IntPtr.Zero)
            {
                var transform = TransformToVisual(null);
                var position = transform.TransformPoint(new global::Windows.Foundation.Point(0, 0));
                var scale = XamlRoot?.RasterizationScale ?? 1.0;

                int x = (int)(position.X * scale);
                int y = (int)(position.Y * scale);
                int width = (int)(finalSize.Width * scale);
                int height = (int)(finalSize.Height * scale);

                if (width > 0 && height > 0)
                {
                    MoveWindow(_childHwnd, x, y, width, height, true);
                }
            }
            else if (finalSize.Width > 0 && finalSize.Height > 0)
            {
                // Try to create the child window if it doesn't exist yet
                CreateChildWindow();
            }

            return result;
        }

        private Microsoft.UI.Xaml.Window? GetParentWindow()
        {
            // In WinUI3 Desktop / MAUI, get window through MAUI application's windows collection
            var mauiApp = Microsoft.Maui.Controls.Application.Current;
            if (mauiApp?.Windows?.Count > 0)
            {
                var mauiWindow = mauiApp.Windows[0];
                if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window xamlWindow)
                {
                    return xamlWindow;
                }
            }

            return null;
        }
    }
}
