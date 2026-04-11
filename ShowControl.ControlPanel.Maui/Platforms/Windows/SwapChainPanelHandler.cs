using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShowControl.Graphics;
using ShowControl.Graphics.Platforms.Windows;
using System.Runtime.InteropServices;
using WinRT;

namespace ShowControl.ControlPanel.Maui.Platforms.Windows;

/// <summary>
/// Platform handler for VulkanSurfaceView on Windows using SwapChainPanel.
/// Enables XAML overlays on top of Vulkan content via DirectX/Vulkan interop.
/// </summary>
public partial class SwapChainPanelHandler : ViewHandler<VulkanSurfaceView, SwapChainPanel>
{
    private D3D11InteropHost? _d3d11Host;
    private bool _swapChainSet;

    public static IPropertyMapper<VulkanSurfaceView, SwapChainPanelHandler> Mapper =
        new PropertyMapper<VulkanSurfaceView, SwapChainPanelHandler>(ViewMapper)
        {
            // SwapChainPanel doesn't support Background property - ignore it
            [nameof(IView.Background)] = (handler, view) => { },
        };

    public SwapChainPanelHandler() : base(Mapper)
    {
    }

    protected override SwapChainPanel CreatePlatformView()
    {
        return new SwapChainPanel();
    }

    protected override void ConnectHandler(SwapChainPanel platformView)
    {
        base.ConnectHandler(platformView);

        platformView.Loaded += OnLoaded;
        platformView.SizeChanged += OnSizeChanged;
        platformView.Unloaded += OnUnloaded;
    }

    protected override void DisconnectHandler(SwapChainPanel platformView)
    {
        platformView.Loaded -= OnLoaded;
        platformView.SizeChanged -= OnSizeChanged;
        platformView.Unloaded -= OnUnloaded;

        if (_d3d11Host != null)
        {
            _d3d11Host.SwapChainRecreatedEvent -= OnSwapChainRecreated;
            _d3d11Host.Dispose();
            _d3d11Host = null;
        }
        _swapChainSet = false;

        base.DisconnectHandler(platformView);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeD3D11AndSwapChain();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var height = e.NewSize.Height;

        if (width > 0 && height > 0)
        {
            // Use logical (DIP) dimensions - SwapChainPanel handles DPI scaling
            VirtualView?.OnSurfaceSizeChanged(width, height);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        VirtualView?.OnSurfaceDestroying();
        _d3d11Host?.Dispose();
        _d3d11Host = null;
        _swapChainSet = false;
    }

    private void InitializeD3D11AndSwapChain()
    {
        if (_swapChainSet) return;

        // Use logical (DIP) dimensions - SwapChainPanel handles DPI scaling
        var width = (uint)PlatformView.ActualWidth;
        var height = (uint)PlatformView.ActualHeight;

        if (width == 0 || height == 0) return;

        try
        {
            // Create D3D11 host
            _d3d11Host = new D3D11InteropHost();
            _d3d11Host.Initialize();
            _d3d11Host.CreateSwapChainForComposition(width, height);

            // Subscribe to swap chain recreated event
            _d3d11Host.SwapChainRecreatedEvent += OnSwapChainRecreated;

            // Set the swap chain on the panel
            SetSwapChainOnPanel(_d3d11Host.SwapChainHandle);
            _swapChainSet = true;

            // Notify that the surface is ready (pass the D3D11 host handle as the surface handle)
            VirtualView?.OnSurfaceReady(_d3d11Host.SwapChainHandle);

            System.Diagnostics.Debug.WriteLine($"[SwapChainPanelHandler] Initialized {width}x{height}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SwapChainPanelHandler] Failed to initialize: {ex}");
        }
    }

    private void OnSwapChainRecreated()
    {
        // Dispatch to UI thread to rebind the swap chain
        PlatformView?.DispatcherQueue.TryEnqueue(() =>
        {
            if (_d3d11Host != null && PlatformView != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SwapChainPanelHandler] Rebinding swap chain after recreation");
                SetSwapChainOnPanel(_d3d11Host.SwapChainHandle);
            }
        });
    }

    private void SetSwapChainOnPanel(nint swapChainPtr)
    {
        // Get ISwapChainPanelNative interface from SwapChainPanel
        var panelNative = PlatformView.As<ISwapChainPanelNative>();
        panelNative.SetSwapChain(swapChainPtr);
    }

    private double GetScaleFactor()
    {
        if (PlatformView?.XamlRoot != null)
        {
            return PlatformView.XamlRoot.RasterizationScale;
        }
        return 1.0;
    }

    /// <summary>
    /// Gets the D3D11 interop host for this handler.
    /// </summary>
    public D3D11InteropHost? D3D11Host => _d3d11Host;
}

/// <summary>
/// ISwapChainPanelNative COM interface for WinUI3.
/// </summary>
[ComImport]
[Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISwapChainPanelNative
{
    [PreserveSig]
    int SetSwapChain(nint swapChain);
}
