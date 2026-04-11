#if WINDOWS
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Runtime.InteropServices;

namespace ShowControl.Graphics.Platforms.Windows;

/// <summary>
/// Manages DirectX 11 resources for Vulkan interop and SwapChain presentation.
/// Creates shared textures that Vulkan can import, and presents them via a composition SwapChain.
/// Implements IDirectCompositionPresenter for use by GraphicsHost.
/// </summary>
public unsafe class D3D11InteropHost : IDirectCompositionPresenter, IDisposable
{
    private readonly D3D11 _d3d11;
    private readonly DXGI _dxgi;

    private ComPtr<ID3D11Device> _device;
    private ComPtr<ID3D11Device1> _device1;
    private ComPtr<ID3D11DeviceContext> _context;
    private ComPtr<IDXGIFactory2> _dxgiFactory;
    private ComPtr<IDXGISwapChain1> _swapChain;
    private ComPtr<ID3D11Texture2D> _sharedTexture;
    private ComPtr<ID3D11Texture2D> _backBuffer;
    private nint _sharedTextureHandle;

    private uint _width;
    private uint _height;
    private bool _disposed;
    private readonly object _presentLock = new();

    public nint SwapChainHandle => (nint)_swapChain.Handle;
    public nint SharedTextureHandle => _sharedTextureHandle;
    public bool IsInitialized => _device.Handle != null;
    public uint Width => _width;
    public uint Height => _height;

    public D3D11InteropHost()
    {
        _d3d11 = D3D11.GetApi();
        _dxgi = DXGI.GetApi();
    }

    /// <summary>
    /// Initializes the D3D11 device and DXGI factory.
    /// </summary>
    public void Initialize()
    {
        // Create D3D11 device with BGRA support for SwapChainPanel
        D3DFeatureLevel[] featureLevels = [D3DFeatureLevel.Level111, D3DFeatureLevel.Level110];
        D3DFeatureLevel actualLevel;

        ID3D11Device* pDevice = null;
        ID3D11DeviceContext* pContext = null;

        fixed (D3DFeatureLevel* pLevels = featureLevels)
        {
            // Try with debug layer first
            var flags = (uint)(CreateDeviceFlag.BgraSupport | CreateDeviceFlag.Debug);
            var hr = _d3d11.CreateDevice(
                (IDXGIAdapter*)null,
                D3DDriverType.Hardware,
                0,
                flags,
                pLevels,
                (uint)featureLevels.Length,
                D3D11.SdkVersion,
                &pDevice,
                &actualLevel,
                &pContext);

            if (hr < 0)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Debug layer not available, creating without debug");
                // Retry without debug layer
                flags = (uint)CreateDeviceFlag.BgraSupport;
                hr = _d3d11.CreateDevice(
                    (IDXGIAdapter*)null,
                    D3DDriverType.Hardware,
                    0,
                    flags,
                    pLevels,
                    (uint)featureLevels.Length,
                    D3D11.SdkVersion,
                    &pDevice,
                    &actualLevel,
                    &pContext);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Debug layer ENABLED");
            }

            SilkMarshal.ThrowHResult(hr);
        }

        _device = new ComPtr<ID3D11Device>(pDevice);
        _context = new ComPtr<ID3D11DeviceContext>(pContext);

        // Get ID3D11Device1 for shared resource support
        var iid = ID3D11Device1.Guid;
        void* device1Ptr;
        SilkMarshal.ThrowHResult(_device.QueryInterface(&iid, &device1Ptr));
        _device1 = new ComPtr<ID3D11Device1>((ID3D11Device1*)device1Ptr);

        // Create DXGI factory
        var dxgiDeviceIid = IDXGIDevice.Guid;
        void* dxgiDevicePtr;
        SilkMarshal.ThrowHResult(_device.QueryInterface(&dxgiDeviceIid, &dxgiDevicePtr));
        var dxgiDevice = new ComPtr<IDXGIDevice>((IDXGIDevice*)dxgiDevicePtr);

        IDXGIAdapter* pAdapter;
        SilkMarshal.ThrowHResult(dxgiDevice.GetAdapter(&pAdapter));
        var adapter = new ComPtr<IDXGIAdapter>(pAdapter);

        var factoryIid = IDXGIFactory2.Guid;
        void* factoryPtr;
        SilkMarshal.ThrowHResult(adapter.GetParent(&factoryIid, &factoryPtr));
        _dxgiFactory = new ComPtr<IDXGIFactory2>((IDXGIFactory2*)factoryPtr);

        adapter.Dispose();
        dxgiDevice.Dispose();

        System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Initialized with feature level {actualLevel}");
    }

    /// <summary>
    /// Creates a SwapChain for composition with the specified size.
    /// </summary>
    public void CreateSwapChainForComposition(uint width, uint height)
    {
        var desc = new SwapChainDesc1
        {
            Width = width,
            Height = height,
            Format = Format.FormatB8G8R8A8Unorm,
            Stereo = 0,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            BufferUsage = DXGI.UsageRenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Premultiplied,
            Flags = 0
        };

        IDXGISwapChain1* pSwapChain;
        SilkMarshal.ThrowHResult(_dxgiFactory.CreateSwapChainForComposition(
            (IUnknown*)_device.Handle,
            &desc,
            (IDXGIOutput*)null,
            &pSwapChain));
        _swapChain = new ComPtr<IDXGISwapChain1>(pSwapChain);

        // Get back buffer and verify actual dimensions
        var texIid = ID3D11Texture2D.Guid;
        void* backBufferPtr;
        SilkMarshal.ThrowHResult(_swapChain.GetBuffer(0, &texIid, &backBufferPtr));
        _backBuffer = new ComPtr<ID3D11Texture2D>((ID3D11Texture2D*)backBufferPtr);

        // Get actual back buffer dimensions (may differ from requested)
        Texture2DDesc backBufferDesc;
        _backBuffer.GetDesc(&backBufferDesc);
        _width = backBufferDesc.Width;
        _height = backBufferDesc.Height;

        System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Created SwapChain: requested {width}x{height}, actual {_width}x{_height}");
    }

    /// <summary>
    /// Creates a shared texture that can be imported by Vulkan.
    /// Returns the KMT handle for Vulkan to import.
    /// </summary>
    public nint CreateSharedTexture(uint width, uint height)
    {
        // Clean up existing shared texture
        // Note: KMT handles from GetSharedHandle are NOT closed with CloseHandle
        // They are kernel-mode transport handles managed by D3D11
        if (_sharedTexture.Handle != null)
        {
            _sharedTextureHandle = 0;  // Just clear reference, don't close
            _sharedTexture.Dispose();
        }

        // Create texture with shared flag for cross-API sharing
        var texDesc = new Texture2DDesc
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatB8G8R8A8Unorm,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.ShaderResource | BindFlag.RenderTarget),
            CPUAccessFlags = 0,
            MiscFlags = (uint)ResourceMiscFlag.Shared  // Basic sharing (uses GetSharedHandle)
        };

        System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Creating shared texture {width}x{height}");

        ID3D11Texture2D* pTexture;
        SilkMarshal.ThrowHResult(_device.CreateTexture2D(&texDesc, (SubresourceData*)null, &pTexture));
        _sharedTexture = new ComPtr<ID3D11Texture2D>(pTexture);

        // Get the shared handle via IDXGIResource (GetSharedHandle for D3D11_RESOURCE_MISC_SHARED)
        var resourceIid = IDXGIResource.Guid;
        void* resourcePtr;
        SilkMarshal.ThrowHResult(_sharedTexture.QueryInterface(&resourceIid, &resourcePtr));
        var dxgiResource = new ComPtr<IDXGIResource>((IDXGIResource*)resourcePtr);

        void* handle;
        SilkMarshal.ThrowHResult(dxgiResource.GetSharedHandle(&handle));
        _sharedTextureHandle = (nint)handle;

        dxgiResource.Dispose();

        System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Created shared texture {width}x{height}, handle=0x{_sharedTextureHandle:X}");

        return _sharedTextureHandle;
    }


    /// <summary>
    /// Copies the shared texture to the SwapChain and presents.
    /// Called after Vulkan has finished rendering (CPU synchronized via fence).
    /// </summary>
    public void CopyAndPresent(ulong acquireKey, ulong releaseKey)
    {
        if (_sharedTexture.Handle == null || _swapChain.Handle == null || _backBuffer.Handle == null)
            return;

        lock (_presentLock)
        {
            // Get dimensions of both textures to ensure they match
            Texture2DDesc srcDesc, dstDesc;
            _sharedTexture.GetDesc(&srcDesc);
            _backBuffer.GetDesc(&dstDesc);

            // Debug: log dimensions periodically
            if (DateTime.Now.Second % 5 == 0 && DateTime.Now.Millisecond < 20)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] CopyAndPresent: sharedTexture={srcDesc.Width}x{srcDesc.Height}, backBuffer={dstDesc.Width}x{dstDesc.Height}");
            }

            if (srcDesc.Width != dstDesc.Width || srcDesc.Height != dstDesc.Height)
            {
                // Dimensions don't match - skip this frame (resize in progress)
                // Just present what we have
                System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Dimension mismatch! shared={srcDesc.Width}x{srcDesc.Height}, back={dstDesc.Width}x{dstDesc.Height}");
                _swapChain.Present(1, 0);
                return;
            }

            // Copy shared texture to back buffer (Vulkan has already finished via fence wait)
            _context.CopyResource((ID3D11Resource*)_backBuffer.Handle, (ID3D11Resource*)_sharedTexture.Handle);

            // Present
            var presentHr = _swapChain.Present(1, 0);
            if (presentHr < 0)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Present failed: 0x{presentHr:X}");
            }
        }
    }

    /// <summary>
    /// Resizes the SwapChain and recreates the shared texture.
    /// Returns the new shared texture handle.
    /// </summary>
    public nint Resize(uint width, uint height)
    {
        if (width == _width && height == _height)
            return _sharedTextureHandle;

        if (width == 0 || height == 0)
            return _sharedTextureHandle;

        lock (_presentLock)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Resize requested: {_width}x{_height} -> {width}x{height}");

            // For swap chain resize, we need to release ALL references first
            _context.ClearState();
            _context.Flush();

            // Release back buffer
            if (_backBuffer.Handle != null)
            {
                _backBuffer.Dispose();
                _backBuffer = default;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Calling ResizeBuffers...");

                var hr = _swapChain.ResizeBuffers(
                    0,  // Keep existing buffer count
                    width,
                    height,
                    Format.FormatUnknown,  // Keep existing format
                    0);

                if (hr < 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] ResizeBuffers failed (0x{hr:X8}), recreating swap chain...");

                    // ResizeBuffers failed - recreate the entire swap chain
                    _swapChain.Dispose();
                    _swapChain = default;

                    var desc = new SwapChainDesc1
                    {
                        Width = width,
                        Height = height,
                        Format = Format.FormatB8G8R8A8Unorm,
                        Stereo = 0,
                        SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
                        BufferUsage = DXGI.UsageRenderTargetOutput,
                        BufferCount = 2,
                        Scaling = Scaling.Stretch,
                        SwapEffect = SwapEffect.FlipSequential,
                        AlphaMode = AlphaMode.Premultiplied,
                        Flags = 0
                    };

                    IDXGISwapChain1* pSwapChain;
                    var createHr = _dxgiFactory.CreateSwapChainForComposition(
                        (IUnknown*)_device.Handle,
                        &desc,
                        (IDXGIOutput*)null,
                        &pSwapChain);

                    if (createHr < 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] CreateSwapChainForComposition failed: 0x{createHr:X8}");
                        return 0;
                    }

                    _swapChain = new ComPtr<IDXGISwapChain1>(pSwapChain);
                    System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Recreated swap chain at {width}x{height}");

                    // Signal that the swap chain was recreated (caller needs to call SetSwapChain again)
                    _swapChainRecreated = true;
                    SwapChainRecreatedEvent?.Invoke();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] ResizeBuffers succeeded");
                }

                // Re-acquire back buffer
                var texIid = ID3D11Texture2D.Guid;
                void* backBufferPtr;
                var getBufferHr = _swapChain.GetBuffer(0, &texIid, &backBufferPtr);
                if (getBufferHr >= 0)
                {
                    _backBuffer = new ComPtr<ID3D11Texture2D>((ID3D11Texture2D*)backBufferPtr);

                    // Get actual back buffer dimensions
                    Texture2DDesc backBufferDesc;
                    _backBuffer.GetDesc(&backBufferDesc);
                    _width = backBufferDesc.Width;
                    _height = backBufferDesc.Height;

                    System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Got back buffer: {_width}x{_height}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] GetBuffer failed with HRESULT: 0x{getBufferHr:X8}");
                    return _sharedTextureHandle; // Keep old shared texture
                }

                // Create shared texture to match actual back buffer size
                var newHandle = CreateSharedTexture(_width, _height);

                System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Resize complete: {_width}x{_height}, handle=0x{newHandle:X}");
                return newHandle;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D11InteropHost] Resize exception: {ex.Message}");
                return _sharedTextureHandle;
            }
        }
    }

    /// <summary>
    /// Indicates if the swap chain was recreated during the last resize.
    /// The caller should call SetSwapChain on the panel again.
    /// </summary>
    public bool SwapChainRecreated
    {
        get
        {
            var result = _swapChainRecreated;
            _swapChainRecreated = false;
            return result;
        }
    }

    /// <summary>
    /// Event raised when the swap chain is recreated and needs to be rebound to the panel.
    /// </summary>
    public event Action? SwapChainRecreatedEvent;

    private bool _swapChainRecreated;

    // IDirectCompositionPresenter.Resize is implemented by the public Resize method that returns nint

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    public void Dispose()
    {
        if (!_disposed)
        {
            // Note: Don't close KMT handles - they're managed by D3D11
            _sharedTextureHandle = 0;
            _sharedTexture.Dispose();
            _backBuffer.Dispose();
            _swapChain.Dispose();
            _dxgiFactory.Dispose();
            _context.Dispose();
            _device1.Dispose();
            _device.Dispose();
            _disposed = true;
        }
    }
}
#endif
