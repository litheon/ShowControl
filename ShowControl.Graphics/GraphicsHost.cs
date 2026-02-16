using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO;
using Silk.NET.Vulkan.Extensions.EXT;
using Device = Silk.NET.Vulkan.Device;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Image = Silk.NET.Vulkan.Image;
using System.Xml.Linq;
using Buffer = Silk.NET.Vulkan.Buffer;
using System.Reflection.Metadata.Ecma335;
using Microsoft.Maui.Graphics;
using System.Threading;
using System.Threading.Tasks;

#if MACCATALYST
using Microsoft.Maui.ApplicationModel;
using UIKit;
using CoreGraphics;
using CoreAnimation;
using Foundation;
using ObjCRuntime;
#endif

#if WINDOWS
using Silk.NET.Maths;
using IWindow = Silk.NET.Windowing.IWindow;
using Silk.NET.Windowing;
#endif

namespace ShowControl.Graphics
{
    public class GraphicsHost : IGraphicsHost
    {
        private Shader _shader = Shader.Kaleidoscope;
        private Thread _backgroundThread;
        public GraphicsHost()
        {
            System.Diagnostics.Debug.WriteLine("[GraphicsHost] Constructor called");
            _backgroundThread = new Thread(Run);
            _backgroundThread.Start();
            System.Diagnostics.Debug.WriteLine("[GraphicsHost] Background thread started");
        }
        [StructLayout(LayoutKind.Sequential)]
        struct PushData
        {
            public Vector2 Resolution;  // width, height
            public float Time;
            private float _pad;         // keep total size multiple of 16

            public float DistortionStrength;
            public float HueTolerance;
            public float Falloff;
            public int ColorCount;

            [MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray, SizeConst = 6)]
            public Vector4Array Colors;

            public PushData()
            {
            }

            //public Vector4 c0;
            //public Vector4 c1;
            //public Vector4 c2;
            //public Vector4 c3;
            //public Vector4 c4;
            //public Vector4 c5;
        }

        [InlineArray(6)]
        public struct Vector4Array // represents float[64], inline
        {
            private Vector4 _element; // the compiler synthesizes the rest
        }

#if WINDOWS
        private Silk.NET.Windowing.IWindow _window;
        private Silk.NET.Windowing.IWindow _presentWindow;
#endif
        private Vk _vk;
        private Instance _instance;
        private SurfaceKHR _surface;
        private KhrSurface _khrSurface;
#if MACCATALYST
        private ExtMetalSurface _extMetalSurface;
#elif WINDOWS
        private KhrWin32Surface _khrWin32Surface;
#endif
        private PhysicalDevice _physicalDevice;
        private Device _device;
        private Queue _graphicsQueue;
        private KhrSwapchain _khrSwapchain;
        private SwapchainKHR _swapchain;
        private Silk.NET.Vulkan.Image[] _swapchainImages;
        private ImageView[] _swapchainImageViews;

        private CommandPool _commandPool;
        private CommandBuffer[] _commandBuffers;

        private RenderPass _renderPass;
        private RenderPass _offscreenRenderPass;
        private Framebuffer[] _framebuffers;

        private KhrSwapchain _swapchainExtension;
        private Pipeline _pipeline;
        private PipelineLayout _pipelineLayout;

        ulong[] _imageSerial;
        private Semaphore[] _imageAvailableSemaphores;
        private Semaphore[] _renderFinishedByImage; // one per swapchain image

        private Semaphore _gpuDoneTL; // produced-by-GPU
        private Semaphore _cpuDoneTL; // released-by-CPU

        // Global counters for each timeline
        private ulong _gpuCtr = 0;
        private ulong _cpuCtr = 0;

        // Per-slot bookkeeping (ring size = StagingRing.RingSize)
        private readonly ulong[] _slotReadyGpuValue = new ulong[StagingRing.RingSize]; // value the CPU should wait for
        private readonly ulong[] _slotNeedCpuRelease = new ulong[StagingRing.RingSize]; // last value that must be released before reuse

        ulong _timelineValue = 0;
        ulong[] _slotSignaledValue = new ulong[3];
        private ulong _cpuSignaled = 0;
        private long _pendingMemoryReads = 0;
        private Fence _submitFence;
        private readonly ManualResetEventSlim _readReadyEvent = new(initialState: true);

        private int _currentFrame = 0;
        private StagingRing _stagingRing;

        private ShaderModule _vertShaderModule;
        private ShaderModule _kaleidoscopeShaderModule;
        private ShaderModule _geometryShaderModule;

        private uint _graphicsQueueIndex;
        private PhysicalDeviceMemoryProperties _memProps;
        private Extent2D _swapExtent;
        private DeviceMemory _offscreenMemory;
        private Silk.NET.Vulkan.Image _offscreenImage;
        private ImageView _offscreenView;
        private Framebuffer _offscreenFramebuffer;
        private ulong _imageSize;

        private bool _imageAcquired = false;
        private uint _acquiredImageIndex = 0;

        private Silk.NET.Vulkan.Buffer _buffer;

        private Stopwatch _stopwatch = new Stopwatch();

        const int MAX_FRAMES_IN_FLIGHT = 3;

        public object _lock = new object();
        public byte[] Bytes = null;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public System.Drawing.Color[] Colors { get; set; } = Array.Empty<System.Drawing.Color>();

        private bool PRESENT = false; // set false to run headless (no window present)
        private Extent2D _offscreenExtent;
        private CommandBuffer[] _cmds;

        private volatile bool _running;
        private Thread _renderThread;

        private unsafe void* _mapped;

        private volatile bool _recreatingSwapchain = false;
        private volatile bool _isClosing = false;
        private volatile bool _tearingDownWSI = false;
        private volatile bool _resizePending = false;
        private bool _previewWindowOpen = false;
        private CancellationTokenSource? _resizeDebounceToken;
        private uint _pendingResizeWidth;
        private uint _pendingResizeHeight;

        public void Run()
        {
            System.Diagnostics.Debug.WriteLine("[GraphicsHost] Run() started");
            try
            {
                //System.Threading.Thread.Sleep(20000);
                System.Diagnostics.Debug.WriteLine("[GraphicsHost] Calling RunHeadless()");
                RunHeadless();
                System.Diagnostics.Debug.WriteLine("[GraphicsHost] RunHeadless() completed");
#if MACCATALYST
                // Auto-open preview window on Mac Catalyst
                System.Diagnostics.Debug.WriteLine("[GraphicsHost] Calling ShowPreviewWindow()");
                ShowPreviewWindow();
                System.Diagnostics.Debug.WriteLine("[GraphicsHost] ShowPreviewWindow() dispatched");
#elif WINDOWS
                ShowHeadlessInWindow();
#endif
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[GraphicsHost] Exception in Run(): {e.Message}");
                System.Diagnostics.Debug.WriteLine($"[GraphicsHost] Stack trace: {e.StackTrace}");
            }
        }

        public void RunHeadless(int width = 800, int height = 600)
        {
            PRESENT = false; // make sure your OnRender uses the no-present path
            _offscreenExtent = new Extent2D((uint)width, (uint)height);
            Width = width;
            Height = height;
            _swapExtent = _offscreenExtent; // reuse same var if your code expects it
            _imageSize = (ulong)width * (ulong)height * 4;

            OnLoadHeadless();  // custom init that skips window/swapchain
            _running = true;
            _renderThread = new Thread(RenderLoop) { IsBackground = true };
            _renderThread.Start();
        }

        private void RenderLoop()
        {
            _stopwatch.Start();
            while (_running)
            {
                OnRender(0);
                Thread.Sleep(0);    // Throttle to prevent 100% CPU usage
            }
        }

        public void StopHeadless()
        {
            _running = false;
            _renderThread?.Join();

        }

#if WINDOWS
        public unsafe void ShowHeadlessInWindow(int width = 800, int height = 600, string title = "Viewer")
        {
            // We assume instance/device already have WSI + swapchain enabled.
            // We also assume your off-screen renderer is already running (RunHeadless or similar).

            var opts = WindowOptions.DefaultVulkan;
            opts.Size = new Vector2D<int>(width, height);
            opts.Title = title;

            var window = Silk.NET.Windowing.Window.Create(opts);
            window.Load += () =>
            {
                // Match the off-screen format you used
                var offFormat = Format.R8G8B8A8Srgb;
                var offColorCS = ColorSpaceKHR.ColorspaceSrgbNonlinearKhr;
                var extent = new Extent2D((uint)width, (uint)height);

                EnablePresentation(window, extent, offFormat, offColorCS);

                _graphicsQueueIndex = CreateOrRecreateSwapchainPinned(window);

                // Tell OnRender to execute the present path (Acquire->Blit->Present)
                PRESENT = true;

                // Optional: drive your render from the window loop instead of a background thread:
                // _running = false; // stop your headless loop if you prefer single-driver
            };

            window.Closing += () =>
            {
                _isClosing = true;
                PRESENT = false;
                _imageAcquired = false;
                _tearingDownWSI = true;
                // Quiesce GPU before destroying swapchain or semaphores
                _vk.QueueWaitIdle(_graphicsQueue);

                // 3) Destroy WSI resources ONLY (keep device/offscreen alive)
                DestroyPerImageSyncObjects(); // destroys _renderFinishedByImage[]
                if (_swapchain.Handle != 0)
                {
                    _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
                    _swapchain = default;
                }
                if (_surface.Handle != 0)
                {
                    _khrSurface.DestroySurface(_instance, _surface, null);
                    _surface = default;
                }

                // 4) Clear swapchain-dependent arrays so OnRender can’t touch them
                _swapchainImages = null;
                _acquiredImageIndex = 0;

                _tearingDownWSI = false;
                _isClosing = false; // allow future re-open if you want
            };

            // Use the window's Render event to call your OnRender (single driving loop),
            // or keep your background loop and just let OnRender see PRESENT=true.
            //window.Render += OnRender;

            window.Run();
        }
        private unsafe void EnablePresentation(IWindow window, Extent2D size, Format format, ColorSpaceKHR colorSpace)
        {
            _presentWindow = window;
            _khrSurface = new KhrSurface(_vk.Context);

            // 1) Surface from existing instance
            _surface = _presentWindow.VkSurface!.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();

            // 2) Swapchain loader
            _swapchainExtension = new KhrSwapchain(_vk.Context);

            // 3) Create swapchain (we blit into it, so TRANSFER_DST is enough)
            var swapchainInfo = new SwapchainCreateInfoKHR(
                surface: _surface,
                minImageCount: 2,
                imageFormat: format,          // must match your off-screen format if you want a straight blit
                imageColorSpace: colorSpace,
                imageExtent: size,
                imageArrayLayers: 1,
                imageUsage: ImageUsageFlags.ImageUsageTransferDstBit,
                imageSharingMode: SharingMode.Exclusive,
                preTransform: SurfaceTransformFlagsKHR.SurfaceTransformIdentityBitKhr,
                compositeAlpha: CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                presentMode: PresentModeKHR.PresentModeFifoKhr,
                clipped: true
            );
            unsafe
            {
                uint extCount = 0;
                _vk.EnumerateDeviceExtensionProperties(_physicalDevice, (byte*)null, ref extCount, null);
                var extProps = new ExtensionProperties[extCount];
                fixed (ExtensionProperties* p = extProps)
                    _vk.EnumerateDeviceExtensionProperties(_physicalDevice, (byte*)null, ref extCount, p);

                bool hasSwapchain = false;
                foreach (var e in extProps)
                    if (SilkMarshal.PtrToString((nint)e.ExtensionName) == "VK_KHR_swapchain")
                    { hasSwapchain = true; break; }

                if (!hasSwapchain) throw new Exception("VK_KHR_swapchain not supported on this device.");
            }
            if (_surface.Handle == 0) throw new Exception("SurfaceKHR is null/invalid.");
            _swapchainExtension.CreateSwapchain(_device, ref swapchainInfo, null, out _swapchain);

            // 4) Fetch swapchain images
            uint count = 0;
            _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, null);
            _imageSerial = new ulong[count];
            _swapchainImages = new Image[count];
            fixed (Image* p = _swapchainImages)
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, &count, p);

            CreatePerImageSyncObjects(count);
        }
#endif

        // Shared methods used by both ARM64 and non-ARM64 presentation code
        private unsafe uint FindPresentQueueFamily(PhysicalDevice pd, SurfaceKHR surface)
        {
            uint count = 0;
            _vk.GetPhysicalDeviceQueueFamilyProperties(pd, ref count, null);
            var props = new QueueFamilyProperties[count];
            fixed (QueueFamilyProperties* p = props)
                _vk.GetPhysicalDeviceQueueFamilyProperties(pd, ref count, p);

            for (uint i = 0; i < count; i++)
            {
                Bool32 supports;
                _khrSurface.GetPhysicalDeviceSurfaceSupport(pd, i, surface, out supports);
                if (supports) return i;
            }
            throw new Exception("No PRESENT-capable queue family.");
        }

        private unsafe void DestroyPerImageSyncObjects()
        {
            if (_renderFinishedByImage != null)
            {
                foreach (var s in _renderFinishedByImage)
                    if (s.Handle != 0) _vk.DestroySemaphore(_device, s, null);
                _renderFinishedByImage = null;
            }
        }

        private unsafe void CreatePerImageSyncObjects(uint imageCount)
        {
            DestroyPerImageSyncObjects(); // safe if null
            _renderFinishedByImage = new Semaphore[imageCount];

            var semInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
            for (int i = 0; i < _renderFinishedByImage.Length; i++)
                _vk.CreateSemaphore(_device, semInfo, null, out _renderFinishedByImage[i]);
        }

        private SurfaceTransformFlagsKHR PickPreTransform(in SurfaceCapabilitiesKHR caps)
        {
            var identity = SurfaceTransformFlagsKHR.SurfaceTransformIdentityBitKhr;
            if ((caps.SupportedTransforms & identity) != 0)
                return identity;

            if (caps.CurrentTransform != 0)
                return caps.CurrentTransform;

            // Fallback: pick the lowest set bit in SupportedTransforms
            foreach (SurfaceTransformFlagsKHR bit in Enum.GetValues(typeof(SurfaceTransformFlagsKHR)))
            {
                if (bit != 0 && (caps.SupportedTransforms & bit) != 0)
                    return bit;
            }

            // Last resort (shouldn't happen on a valid surface):
            return identity;
        }

#if WINDOWS
        private unsafe uint CreateOrRecreateSwapchainPinned(IWindow window)
        {
            _vk.QueueWaitIdle(_graphicsQueue);

            if (_surface.Handle == 0) throw new Exception("Surface not created. Call EnablePresentation first.");


            // --- Surface caps / formats / modes ---
            _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var caps);
            Extent2D extent;
            if (caps.CurrentExtent.Width != uint.MaxValue)
            {
                extent = caps.CurrentExtent;
            }
            else
            {
                var w = (uint)window.Size.X;
                var h = (uint)window.Size.Y;
                if (w == 0 || h == 0) return uint.MaxValue;  // <-- don’t create a swapchain now
                extent = new Extent2D(
                    Math.Clamp(w, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                    Math.Clamp(h, caps.MinImageExtent.Height, caps.MaxImageExtent.Height)
                );
            }

            uint fmtCount = 0;
            if (_khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, null) != Result.Success)
            {
                throw new Exception("Something failed");
            }
            var fmts = new SurfaceFormatKHR[fmtCount];
            fixed (SurfaceFormatKHR* p = fmts)
            {
                if (_khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, p) != Result.Success)
                {
                    throw new Exception("Something failed");
                }
            }
            if (fmtCount == 0) throw new Exception("No surface formats.");

            uint modeCount = 0; _khrSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, ref modeCount, null);
            var modes = new PresentModeKHR[modeCount];
            fixed (PresentModeKHR* p = modes) _khrSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, ref modeCount, p);
            if (modeCount == 0) throw new Exception("No present modes.");

            // Prefer B8G8R8A8_SRGB to match your off-screen format
            var pickFmt = fmts[0];
            foreach (var f in fmts)
                if (f.Format == Format.R8G8B8A8Srgb && f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                { pickFmt = f; break; }

            var presentMode = PresentModeKHR.PresentModeFifoKhr; // always supported

            // Image count
            uint imageCount = Math.Max(2, caps.MinImageCount);
            if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount) imageCount = caps.MaxImageCount;

            // Queue family setup
            uint gfxQ = 0;                // your function
            uint presentQ = FindPresentQueueFamily(_physicalDevice, _surface); // see helper below

            var preTransform = PickPreTransform(caps);
            var sci = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = pickFmt.Format,
                ImageColorSpace = pickFmt.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ImageUsageTransferDstBit, // we blit into it
                PreTransform = preTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                PresentMode = presentMode,
                Clipped = true,
                OldSwapchain = _swapchain
            };

            Result res;

            if (gfxQ == presentQ)
            {
                // Exclusive sharing, no indices pointer
                sci.ImageSharingMode = SharingMode.Exclusive;
                sci.QueueFamilyIndexCount = 0;
                sci.PQueueFamilyIndices = null;

                res = _swapchainExtension.CreateSwapchain(_device, ref sci, null, out var newSwap);
                if (res != Result.Success) throw new Exception($"vkCreateSwapchainKHR failed: {res}");
                if (_swapchain.Handle != 0)
                {
                    _vk.QueueWaitIdle(_graphicsQueue);
                    DestroyPerImageSyncObjects();
                    _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
                }
                _swapchain = newSwap;
            }
            else
            {
                // Concurrent sharing: PIN the array across the call
                uint[] indices = new uint[] { gfxQ, presentQ };
                fixed (uint* pIdx = indices)
                {
                    sci.ImageSharingMode = SharingMode.Concurrent;
                    sci.QueueFamilyIndexCount = 2;
                    sci.PQueueFamilyIndices = pIdx;

                    res = _swapchainExtension.CreateSwapchain(_device, ref sci, null, out var newSwap);
                    if (res != Result.Success) throw new Exception($"vkCreateSwapchainKHR failed: {res}");
                    if (_swapchain.Handle != 0)
                    {
                        DestroyPerImageSyncObjects();
                        _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
                    }
                    _swapchain = newSwap;
                } // <- pointer stays valid until after the call returns
            }

            // Fetch images
            uint count = 0;
            _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, null);
            _swapchainImages = new Image[count];
            fixed (Image* pImgs = _swapchainImages)
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, pImgs);

            _imageAcquired = false;
            _acquiredImageIndex = 0;

            DestroyPerImageSyncObjects();
            _renderFinishedByImage = new Semaphore[count];
            var semInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
            for (int i = 0; i < _renderFinishedByImage.Length; i++)
                _vk.CreateSemaphore(_device, semInfo, null, out _renderFinishedByImage[i]);

            _swapExtent = extent; // keep for blit

            return gfxQ;
        }
#endif

#if MACCATALYST
        private nint _metalLayerPtr;
        private uint _metalSurfaceWidth;
        private uint _metalSurfaceHeight;

        /// <summary>
        /// Creates a Vulkan surface from a CAMetalLayer pointer and sets up the swapchain for presentation.
        /// Called when the MetalSurfaceView's layer is ready.
        /// </summary>
        public unsafe void EnableMetalPresentation(nint metalLayerPtr, uint width, uint height)
        {
            if (_surface.Handle != 0)
            {
                // Surface already exists, just resize
                HandleMetalSurfaceResize(width, height);
                return;
            }

            _metalLayerPtr = metalLayerPtr;
            _metalSurfaceWidth = width;
            _metalSurfaceHeight = height;

            // Create Metal surface using VK_EXT_metal_surface
            var createInfo = new MetalSurfaceCreateInfoEXT
            {
                SType = StructureType.MetalSurfaceCreateInfoExt,
                PLayer = (nint*)metalLayerPtr
            };

            var result = _extMetalSurface.CreateMetalSurface(_instance, &createInfo, null, out _surface);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create Metal surface: {result}");
            }

            // Initialize swapchain extension
            _swapchainExtension = new KhrSwapchain(_vk.Context);

            // Create swapchain
            CreateSwapchainForMetalSurface(width, height);

            // Enable presentation path in render loop
            PRESENT = true;
        }

        /// <summary>
        /// Handles resize of the Metal surface by recreating the swapchain.
        /// Debounces rapid resize events to avoid constant swapchain recreation.
        /// </summary>
        public void HandleMetalSurfaceResize(uint width, uint height)
        {
            if (width == 0 || height == 0) return;
            if (_surface.Handle == 0) return;

            _metalSurfaceWidth = width;
            _metalSurfaceHeight = height;
            _pendingResizeWidth = width;
            _pendingResizeHeight = height;
            _resizePending = true;  // Skip preview rendering while resize pending

            // Cancel any pending resize
            _resizeDebounceToken?.Cancel();
            _resizeDebounceToken = new CancellationTokenSource();
            var token = _resizeDebounceToken.Token;

            // Debounce: wait 100ms after last resize before recreating swapchain
            Task.Run(async () =>
            {
                try
                {
                     await Task.Delay(100, token);
                    if (!token.IsCancellationRequested)
                    {
                        RecreateSwapchainForSize(_pendingResizeWidth, _pendingResizeHeight);
                        _resizePending = false;  // Resume preview rendering
                    }
                }
                catch (TaskCanceledException)
                {
                    // Expected when resize is cancelled by a new resize
                }
            }, token).ContinueWith((t) => {
                if (t.IsFaulted)
                {
                    var exception = t.Exception.ToString();
                }
            });
        }

        /// <summary>
        /// Creates or recreates the swapchain for the Metal surface.
        /// </summary>
        private unsafe void CreateSwapchainForMetalSurface(uint width, uint height)
        {
            // Query surface capabilities
            _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var caps);

            // Clamp extent to surface capabilities
            var extent = new Extent2D(
                Math.Clamp(width, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                Math.Clamp(height, caps.MinImageExtent.Height, caps.MaxImageExtent.Height)
            );

            // Query supported formats
            uint fmtCount = 0;
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, null);
            var fmts = new SurfaceFormatKHR[fmtCount];
            fixed (SurfaceFormatKHR* p = fmts)
                _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, p);

            // On Metal, prefer B8G8R8A8_SRGB (native format), then fall back to R8G8B8A8_SRGB
            var pickFmt = fmts[0];
            bool foundPreferred = false;
            foreach (var f in fmts)
            {
                // First choice: B8G8R8A8 (Metal native)
                if (f.Format == Format.B8G8R8A8Srgb && f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                {
                    pickFmt = f;
                    foundPreferred = true;
                    break;
                }
                // Second choice: R8G8B8A8
                if (!foundPreferred && f.Format == Format.R8G8B8A8Srgb && f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                {
                    pickFmt = f;
                }
            }

            // Image count
            uint imageCount = Math.Max(2, caps.MinImageCount);
            if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
                imageCount = caps.MaxImageCount;

            var preTransform = PickPreTransform(caps);

            var sci = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = pickFmt.Format,
                ImageColorSpace = pickFmt.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ImageUsageTransferDstBit, // We blit into it
                ImageSharingMode = SharingMode.Exclusive,
                PreTransform = preTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                PresentMode = PresentModeKHR.PresentModeFifoKhr, // FIFO is always supported
                Clipped = true,
                OldSwapchain = default
            };

            var result = _swapchainExtension.CreateSwapchain(_device, ref sci, null, out _swapchain);
            if (result != Result.Success)
                throw new Exception($"vkCreateSwapchainKHR failed: {result}");

            // Fetch swapchain images
            uint count = 0;
            _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, null);
            _swapchainImages = new Image[count];
            _imageSerial = new ulong[count];
            fixed (Image* pImgs = _swapchainImages)
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, pImgs);

            CreatePerImageSyncObjects(count);
            _swapExtent = extent;
        }

        /// <summary>
        /// Recreates the swapchain with a new size.
        /// </summary>
        private unsafe void RecreateSwapchainForSize(uint width, uint height)
        {
            // Stop OnRender from running during swapchain recreation
            _recreatingSwapchain = true;
            _imageAcquired = false;
            _vk.QueueWaitIdle(_graphicsQueue);

            try
            {
            // Query surface capabilities
            _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var caps);

            System.Diagnostics.Debug.WriteLine($"[GraphicsHost] RecreateSwapchain: requested={width}x{height}, caps.current={caps.CurrentExtent.Width}x{caps.CurrentExtent.Height}, caps.min={caps.MinImageExtent.Width}x{caps.MinImageExtent.Height}, caps.max={caps.MaxImageExtent.Width}x{caps.MaxImageExtent.Height}");

            // On Metal, use the requested size directly since CAMetalLayer.drawableSize is already set
            var extent = new Extent2D(width, height);

            // Query formats again
            uint fmtCount = 0;
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, null);
            var fmts = new SurfaceFormatKHR[fmtCount];
            fixed (SurfaceFormatKHR* p = fmts)
                _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, p);

            // On Metal, prefer B8G8R8A8_SRGB (native format), then fall back to R8G8B8A8_SRGB
            var pickFmt = fmts[0];
            bool foundPreferred = false;
            foreach (var f in fmts)
            {
                // First choice: B8G8R8A8 (Metal native)
                if (f.Format == Format.B8G8R8A8Srgb && f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                {
                    pickFmt = f;
                    foundPreferred = true;
                    break;
                }
                // Second choice: R8G8B8A8
                if (!foundPreferred && f.Format == Format.R8G8B8A8Srgb && f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                {
                    pickFmt = f;
                }
            }

            uint imageCount = Math.Max(2, caps.MinImageCount);
            if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
                imageCount = caps.MaxImageCount;

            var preTransform = PickPreTransform(caps);

            var sci = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = pickFmt.Format,
                ImageColorSpace = pickFmt.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ImageUsageTransferDstBit,
                ImageSharingMode = SharingMode.Exclusive,
                PreTransform = preTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                PresentMode = PresentModeKHR.PresentModeFifoKhr,
                Clipped = true,
                OldSwapchain = _swapchain // Pass old swapchain for safe recreation
            };

            var result = _swapchainExtension.CreateSwapchain(_device, ref sci, null, out var newSwapchain);
            if (result != Result.Success)
                throw new Exception($"vkCreateSwapchainKHR failed: {result}");

            // Destroy old swapchain after creating new one
            if (_swapchain.Handle != 0)
            {
                DestroyPerImageSyncObjects();
                _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
            }
            _swapchain = newSwapchain;

            // Fetch new swapchain images
            uint count = 0;
            _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, null);
            _swapchainImages = new Image[count];
            _imageSerial = new ulong[count];
            fixed (Image* pImgs = _swapchainImages)
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, pImgs);

            CreatePerImageSyncObjects(count);
            _swapExtent = extent;
            _imageAcquired = false;
            }
            finally
            {
                // Resume OnRender
                _recreatingSwapchain = false;
            }
        }

        /// <summary>
        /// Cleans up Metal presentation resources when the window is closing.
        /// </summary>
        public unsafe void CleanupMetalPresentation()
        {
            PRESENT = false;
            _imageAcquired = false;
            _tearingDownWSI = true;

            _vk.QueueWaitIdle(_graphicsQueue);

            DestroyPerImageSyncObjects();

            if (_swapchain.Handle != 0)
            {
                _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
                _swapchain = default;
            }

            if (_surface.Handle != 0)
            {
                _khrSurface.DestroySurface(_instance, _surface, null);
                _surface = default;
            }

            _swapchainImages = null;
            _metalLayerPtr = 0;
            _tearingDownWSI = false;
        }

        /// <summary>
        /// Event raised when the graphics host wants a preview window to be opened.
        /// The MAUI app should handle this event and create the window.
        /// </summary>
        public event EventHandler<PreviewWindowRequestedEventArgs>? PreviewWindowRequested;

        /// <summary>
        /// Requests the MAUI app to open a preview window.
        /// The app should handle the PreviewWindowRequested event.
        /// </summary>
        public void ShowPreviewWindow()
        {
            if (_previewWindowOpen)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine("[GraphicsHost] Requesting preview window from MAUI app...");
            _previewWindowOpen = true;

            // Wait for event handler to be subscribed (race condition with MainPage.Initialize)
            Task.Run(async () =>
            {
                // Wait up to 5 seconds for a handler to be subscribed
                for (int i = 0; i < 50 && PreviewWindowRequested == null; i++)
                {
                    await Task.Delay(100);
                }

                if (PreviewWindowRequested == null)
                {
                    System.Diagnostics.Debug.WriteLine("[GraphicsHost] No handler subscribed to PreviewWindowRequested after 5 seconds");
                    _previewWindowOpen = false;
                    return;
                }

                // Raise event on main thread
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    PreviewWindowRequested?.Invoke(this, new PreviewWindowRequestedEventArgs(
                        _offscreenExtent.Width,
                        _offscreenExtent.Height
                    ));
                });
            });
        }

        /// <summary>
        /// Called by the MAUI app when the preview window is closed.
        /// </summary>
        public void OnPreviewWindowClosed()
        {
            System.Diagnostics.Debug.WriteLine("[GraphicsHost] Preview window closed");
            CleanupMetalPresentation();
            _previewWindowOpen = false;
        }

        // Win32 stubs for Mac Catalyst (not supported)
        public void EnableWin32Presentation(nint hwnd, uint width, uint height)
        {
            // Not supported on this platform
        }

        public void HandleWin32SurfaceResize(uint width, uint height)
        {
            // Not supported on this platform
        }

    /// <summary>
    /// UIView subclass with CAMetalLayer as its backing layer.
    /// </summary>
    private class MetalBackedUIView : UIView
    {
        public event EventHandler<nint>? LayerReady;
        public event EventHandler<CGSize>? SizeChanged;

        private bool _layerReported = false;

        public MetalBackedUIView() : base()
        {
            ContentScaleFactor = UIScreen.MainScreen.Scale;
            BackgroundColor = UIColor.Black;
        }

        [Export("layerClass")]
        public static Class LayerClass()
        {
            return new Class(typeof(CAMetalLayer));
        }

        public CAMetalLayer MetalLayer => (CAMetalLayer)Layer;

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();

            var newSize = new CGSize(
                Bounds.Width * ContentScaleFactor,
                Bounds.Height * ContentScaleFactor
            );

            if (newSize.Width > 0 && newSize.Height > 0)
            {
                MetalLayer.DrawableSize = newSize;
                SizeChanged?.Invoke(this, newSize);

                if (!_layerReported)
                {
                    _layerReported = true;
                    LayerReady?.Invoke(this, MetalLayer.Handle);
                }
            }
        }

        public override void MovedToWindow()
        {
            base.MovedToWindow();

            if (Window != null && !_layerReported && Bounds.Width > 0 && Bounds.Height > 0)
            {
                _layerReported = true;
                MetalLayer.DrawableSize = new CGSize(
                    Bounds.Width * ContentScaleFactor,
                    Bounds.Height * ContentScaleFactor
                );
                LayerReady?.Invoke(this, MetalLayer.Handle);
            }
        }
    }
#else
        // Stub implementations for non-MACCATALYST builds
        public event EventHandler<PreviewWindowRequestedEventArgs>? PreviewWindowRequested;

        public void EnableMetalPresentation(nint metalLayerPtr, uint width, uint height)
        {
            // Not supported on this platform
        }

        public void HandleMetalSurfaceResize(uint width, uint height)
        {
            // Not supported on this platform
        }

#if WINDOWS
        public unsafe void OnPreviewWindowClosed()
        {
            PRESENT = false;
            _imageAcquired = false;
            _tearingDownWSI = true;

            _vk.QueueWaitIdle(_graphicsQueue);

            DestroyPerImageSyncObjects();
            if (_swapchain.Handle != 0)
            {
                _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
                _swapchain = default;
            }
            if (_surface.Handle != 0)
            {
                _khrSurface.DestroySurface(_instance, _surface, null);
                _surface = default;
            }

            _swapchainImages = null;
            _acquiredImageIndex = 0;
            _tearingDownWSI = false;
            _previewWindowOpen = false;
        }

        private nint _win32Hwnd;
        private uint _win32SurfaceWidth;
        private uint _win32SurfaceHeight;

        /// <summary>
        /// Creates a Vulkan surface from a Win32 HWND and sets up the swapchain for presentation.
        /// </summary>
        public unsafe void EnableWin32Presentation(nint hwnd, uint width, uint height)
        {
            if (_surface.Handle != 0)
            {
                // Surface already exists, just resize
                HandleWin32SurfaceResize(width, height);
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[GraphicsHost] EnableWin32Presentation: hwnd=0x{hwnd:X}, size={width}x{height}");

            _win32Hwnd = hwnd;
            _win32SurfaceWidth = width;
            _win32SurfaceHeight = height;

            // Create Win32 surface
            _khrSurface = new KhrSurface(_vk.Context);

            var createInfo = new Win32SurfaceCreateInfoKHR
            {
                SType = StructureType.Win32SurfaceCreateInfoKhr,
                Hinstance = System.Diagnostics.Process.GetCurrentProcess().Handle,
                Hwnd = hwnd
            };

            var result = _khrWin32Surface.CreateWin32Surface(_instance, &createInfo, null, out _surface);
            if (result != Result.Success)
                throw new Exception($"vkCreateWin32SurfaceKHR failed: {result}");

            System.Diagnostics.Debug.WriteLine($"[GraphicsHost] Win32 surface created: {_surface.Handle}");

            // Create swapchain
            _swapchainExtension = new KhrSwapchain(_vk.Context);
            CreateSwapchainForWin32Surface(width, height);

            // Enable presentation
            PRESENT = true;
            _previewWindowOpen = true;
        }

        private unsafe void CreateSwapchainForWin32Surface(uint width, uint height)
        {
            // Query surface capabilities
            _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var caps);

            var extent = new Extent2D(
                Math.Clamp(width, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                Math.Clamp(height, caps.MinImageExtent.Height, caps.MaxImageExtent.Height)
            );

            // Query formats
            uint fmtCount = 0;
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, null);
            var fmts = new SurfaceFormatKHR[fmtCount];
            fixed (SurfaceFormatKHR* p = fmts)
                _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, p);

            // Prefer B8G8R8A8 or R8G8B8A8 SRGB
            var pickFmt = fmts[0];
            foreach (var f in fmts)
            {
                if ((f.Format == Format.B8G8R8A8Srgb || f.Format == Format.R8G8B8A8Srgb) &&
                    f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                {
                    pickFmt = f;
                    break;
                }
            }

            uint imageCount = Math.Max(2, caps.MinImageCount);
            if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
                imageCount = caps.MaxImageCount;

            var preTransform = PickPreTransform(caps);

            var sci = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = pickFmt.Format,
                ImageColorSpace = pickFmt.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ImageUsageTransferDstBit,
                ImageSharingMode = SharingMode.Exclusive,
                PreTransform = preTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                PresentMode = PresentModeKHR.PresentModeFifoKhr,
                Clipped = true,
                OldSwapchain = default
            };

            var result = _swapchainExtension.CreateSwapchain(_device, ref sci, null, out _swapchain);
            if (result != Result.Success)
                throw new Exception($"vkCreateSwapchainKHR failed: {result}");

            // Fetch swapchain images
            uint count = 0;
            _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, null);
            _swapchainImages = new Image[count];
            _imageSerial = new ulong[count];
            fixed (Image* pImgs = _swapchainImages)
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, pImgs);

            CreatePerImageSyncObjects(count);
            _swapExtent = extent;
        }

        /// <summary>
        /// Handles resize of the Win32 surface by recreating the swapchain.
        /// </summary>
        public void HandleWin32SurfaceResize(uint width, uint height)
        {
            if (width == 0 || height == 0) return;
            if (_surface.Handle == 0) return;

            _win32SurfaceWidth = width;
            _win32SurfaceHeight = height;
            _pendingResizeWidth = width;
            _pendingResizeHeight = height;
            _resizePending = true;

            // Cancel any pending resize
            _resizeDebounceToken?.Cancel();
            _resizeDebounceToken = new CancellationTokenSource();
            var token = _resizeDebounceToken.Token;

            // Debounce resize
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100, token);
                    if (!token.IsCancellationRequested)
                    {
                        RecreateWin32Swapchain(_pendingResizeWidth, _pendingResizeHeight);
                        _resizePending = false;
                    }
                }
                catch (TaskCanceledException) { }
            }, token);
        }

        private unsafe void RecreateWin32Swapchain(uint width, uint height)
        {
            _recreatingSwapchain = true;
            _imageAcquired = false;
            _vk.QueueWaitIdle(_graphicsQueue);

            try
            {
                // Query surface capabilities
                _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var caps);

                var extent = new Extent2D(
                    Math.Clamp(width, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                    Math.Clamp(height, caps.MinImageExtent.Height, caps.MaxImageExtent.Height)
                );

                // Query formats
                uint fmtCount = 0;
                _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, null);
                var fmts = new SurfaceFormatKHR[fmtCount];
                fixed (SurfaceFormatKHR* p = fmts)
                    _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref fmtCount, p);

                var pickFmt = fmts[0];
                foreach (var f in fmts)
                {
                    if ((f.Format == Format.B8G8R8A8Srgb || f.Format == Format.R8G8B8A8Srgb) &&
                        f.ColorSpace == ColorSpaceKHR.ColorspaceSrgbNonlinearKhr)
                    {
                        pickFmt = f;
                        break;
                    }
                }

                uint imageCount = Math.Max(2, caps.MinImageCount);
                if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
                    imageCount = caps.MaxImageCount;

                var preTransform = PickPreTransform(caps);

                var sci = new SwapchainCreateInfoKHR
                {
                    SType = StructureType.SwapchainCreateInfoKhr,
                    Surface = _surface,
                    MinImageCount = imageCount,
                    ImageFormat = pickFmt.Format,
                    ImageColorSpace = pickFmt.ColorSpace,
                    ImageExtent = extent,
                    ImageArrayLayers = 1,
                    ImageUsage = ImageUsageFlags.ImageUsageTransferDstBit,
                    ImageSharingMode = SharingMode.Exclusive,
                    PreTransform = preTransform,
                    CompositeAlpha = CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                    PresentMode = PresentModeKHR.PresentModeFifoKhr,
                    Clipped = true,
                    OldSwapchain = _swapchain
                };

                var result = _swapchainExtension.CreateSwapchain(_device, ref sci, null, out var newSwapchain);
                if (result != Result.Success)
                    throw new Exception($"vkCreateSwapchainKHR failed: {result}");

                // Destroy old swapchain
                if (_swapchain.Handle != 0)
                {
                    DestroyPerImageSyncObjects();
                    _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
                }
                _swapchain = newSwapchain;

                // Fetch new swapchain images
                uint count = 0;
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, null);
                _swapchainImages = new Image[count];
                _imageSerial = new ulong[count];
                fixed (Image* pImgs = _swapchainImages)
                    _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, pImgs);

                CreatePerImageSyncObjects(count);
                _swapExtent = extent;
                _imageAcquired = false;
            }
            finally
            {
                _recreatingSwapchain = false;
            }
        }
#else
        // Stubs for non-Windows platforms (e.g., plain net8.0)
        public void OnPreviewWindowClosed()
        {
            PRESENT = false;
            _previewWindowOpen = false;
        }

        public void EnableWin32Presentation(nint hwnd, uint width, uint height)
        {
            // Not supported on this platform
        }

        public void HandleWin32SurfaceResize(uint width, uint height)
        {
            // Not supported on this platform
        }
#endif
#endif

        private unsafe bool TryPickDeviceAndQueuesPreWindow(out PhysicalDevice picked, out uint gfxQ, out uint presentQ)
        {
            picked = default; gfxQ = presentQ = uint.MaxValue;

            uint devCount = 0;
            _vk.EnumeratePhysicalDevices(_instance, ref devCount, null);
            if (devCount == 0) return false;

            var devs = new PhysicalDevice[devCount];
            fixed (PhysicalDevice* p = devs) _vk.EnumeratePhysicalDevices(_instance, &devCount, p);

            foreach (var pd in devs)
            {
                if (!DeviceHasSwapchain(pd)) continue;

                uint qCount = 0;
                _vk.GetPhysicalDeviceQueueFamilyProperties(pd, ref qCount, null);
                var qprops = new QueueFamilyProperties[qCount];
                fixed (QueueFamilyProperties* qp = qprops)
                    _vk.GetPhysicalDeviceQueueFamilyProperties(pd, ref qCount, qp);

                uint g = uint.MaxValue, pIdx = uint.MaxValue;

                for (uint i = 0; i < qCount; i++)
                {
                    if (g == uint.MaxValue && (qprops[i].QueueFlags & QueueFlags.QueueGraphicsBit) != 0)
                        g = i;

                    // This is the key call: does this family support presenting to the Win32 desktop?
                    // No VkSurfaceKHR or window handle is required.
#if WINDOWS
                    if (pIdx == uint.MaxValue && _khrWin32Surface.GetPhysicalDeviceWin32PresentationSupport(pd, i))
                        pIdx = i;
#else
                    // On non-Windows platforms, just pick any queue family for present
                    if (pIdx == uint.MaxValue)
                        pIdx = i;
#endif
                }

                if (g != uint.MaxValue && pIdx != uint.MaxValue)
                {
                    picked = pd; gfxQ = g; presentQ = pIdx;
                    return true;
                }
            }

            return false;
        }

        private unsafe bool DeviceHasSwapchain(PhysicalDevice pd)
        {
            uint count = 0;
            _vk.EnumerateDeviceExtensionProperties(pd, (byte*)null, ref count, null);
            var props = new ExtensionProperties[count];
            fixed (ExtensionProperties* p = props)
                _vk.EnumerateDeviceExtensionProperties(pd, (byte*)null, ref count, p);

            for (int i = 0; i < props.Length; i++)
            {
                fixed (byte* pExtensionName = props[i].ExtensionName)
                {
                    var name = SilkMarshal.PtrToString((nint)pExtensionName);
                    if (name == "VK_KHR_swapchain") return true;
                }
            }
            return false;
        }
        private unsafe void OnLoadHeadless()
        {
            _vk = Vk.GetApi();

            // --- Instance ---
            var appInfo = new ApplicationInfo(
                pApplicationName: (byte*)SilkMarshal.StringToPtr("Headless"),
                applicationVersion: new Version32(1, 0, 0),
                pEngineName: (byte*)SilkMarshal.StringToPtr("NoEngine"),
                engineVersion: new Version32(1, 0, 0),
                apiVersion: Vk.Version12
            );

            // Instance extensions: none needed for pure headless.
            // On macOS/MoltenVK you likely need portability enumeration:
            var instExts = new List<string>();
#if MACCATALYST
            //instExts.Add("VK_KHR_portability_enumeration");
            instExts.Add("VK_EXT_metal_surface");  // Standard extension for Metal interop
#else
            instExts.Add("VK_KHR_win32_surface");
#endif
            instExts.Add("VK_KHR_surface");
            instExts.Add("VK_EXT_debug_utils");
            var instExtPtr = SilkMarshal.StringArrayToPtr(instExts.ToArray());
            var instFlags = InstanceCreateFlags.None;

            var layers = new List<string>();
#if MACCATALYST
            instFlags |= (InstanceCreateFlags)InstanceCreateFlags.InstanceCreateEnumeratePortabilityBitKhr;
#else
            layers.Add("VK_LAYER_KHRONOS_validation");
#endif

            
            
            var layersPtr = SilkMarshal.StringArrayToPtr(layers.ToArray());

            var instanceCI = new InstanceCreateInfo(
                pApplicationInfo: &appInfo,
                flags: instFlags,
                enabledExtensionCount: (uint)instExts.Count,
                ppEnabledExtensionNames: (byte**)instExtPtr,
                enabledLayerCount: (uint)layers.Count,
                ppEnabledLayerNames: (byte**)layersPtr
            );
            var result = _vk.CreateInstance(instanceCI, null, out _instance);
            if (result != Result.Success)
            {
                System.Diagnostics.Debug.Print(result.ToString());
                Console.WriteLine(result.ToString());
                throw new Exception("Failed");
            }

            _khrSurface = new KhrSurface(_vk.Context);
#if WINDOWS
            _khrWin32Surface = new KhrWin32Surface(_vk.Context);
#elif MACCATALYST
            _extMetalSurface = new ExtMetalSurface(_vk.Context);
#endif

            // --- Physical device & queue family ---
            if (!TryPickDeviceAndQueuesPreWindow(out _physicalDevice, out uint graphicsQueue, out uint presentQueue))
                throw new Exception("No device has GRAPHICS and Win32 PRESENT support.");

            _graphicsQueueIndex = graphicsQueue;

            // --- Logical device ---
            float prio = 1f;
            var qCI = new DeviceQueueCreateInfo(queueFamilyIndex: graphicsQueue, queueCount: 1, pQueuePriorities: &prio);

            var devExts = new List<string>();
            devExts.Add("VK_KHR_swapchain");
            devExts.Add("VK_KHR_timeline_semaphore");
#if MACCATALYST
            // MoltenVK requires this device extension
            devExts.Add("VK_KHR_portability_subset");
#endif
            var devExtPtr = SilkMarshal.StringArrayToPtr(devExts.ToArray());

            PhysicalDeviceVulkan12Features v12 = new()
            {
                SType = StructureType.PhysicalDeviceVulkan12Features,
                TimelineSemaphore = Vk.True // <-- REQUIRED
            };

            var devCI = new DeviceCreateInfo(
                queueCreateInfoCount: 1,
                pQueueCreateInfos: &qCI,
                enabledExtensionCount: (uint)devExts.Count,
                ppEnabledExtensionNames: (byte**)devExtPtr,
                pNext: &v12
            );
            _vk.CreateDevice(_physicalDevice, devCI, null, out _device);
            _vk.GetDeviceQueue(_device, graphicsQueue, 0, out _graphicsQueue);

            _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out _memProps);

            // --- Sync objects (double-buffered fence & semaphores reused even if headless) ---
            _imageSerial = new ulong[MAX_FRAMES_IN_FLIGHT];
            _imageAvailableSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            var fenceInfo = new FenceCreateInfo(flags: FenceCreateFlags.FenceCreateSignaledBit);
            var semInfo = new SemaphoreCreateInfo()
            {
                SType = StructureType.SemaphoreCreateInfo
            };
            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                _vk.CreateSemaphore(_device, semInfo, null, out _imageAvailableSemaphores[i]);
            }

            
            SemaphoreTypeCreateInfo timelineType = new()
            {
                SType = StructureType.SemaphoreTypeCreateInfo,
                SemaphoreType = SemaphoreType.Timeline,
                InitialValue = _timelineValue
            };
            SemaphoreCreateInfo semCI = new() { SType = StructureType.SemaphoreCreateInfo, PNext = &timelineType };

            // Create two timeline semaphores
            SemaphoreTypeCreateInfo tlType = new()
            {
                SType = StructureType.SemaphoreTypeCreateInfo,
                SemaphoreType = SemaphoreType.Timeline,
                InitialValue = 0   // both start at 0
            };
            SemaphoreCreateInfo tlCI = new()
            {
                SType = StructureType.SemaphoreCreateInfo,
                PNext = &tlType
            };
            _vk.CreateSemaphore(_device, &tlCI, null, out _gpuDoneTL);
            _vk.CreateSemaphore(_device, &tlCI, null, out _cpuDoneTL);

            var fi = new FenceCreateInfo(flags: FenceCreateFlags.FenceCreateSignaledBit);
            _vk.CreateFence(_device, fi, null, out _submitFence);

            _gpuCtr = 0;
            _cpuCtr = 0;

            // --- Command pool & a single primary CB ---
            var poolCI = new CommandPoolCreateInfo(
                queueFamilyIndex: graphicsQueue,
                flags: CommandPoolCreateFlags.CommandPoolCreateResetCommandBufferBit
            );
            _vk.CreateCommandPool(_device, poolCI, null, out _commandPool);

            // Create command buffers
            _cmds = new CommandBuffer[MAX_FRAMES_IN_FLIGHT];
            var allocInfo = new CommandBufferAllocateInfo(
                commandPool: _commandPool,
                level: CommandBufferLevel.Primary,
                commandBufferCount: (uint)MAX_FRAMES_IN_FLIGHT);
            fixed (CommandBuffer* p = _cmds)
                _vk.AllocateCommandBuffers(_device, ref allocInfo, p);

            var allocOne = new CommandBufferAllocateInfo(
                commandPool: _commandPool,
                level: CommandBufferLevel.Primary,
                commandBufferCount: 1
            );

            // --- Off-screen image (use one format consistently) ---
            var offFormat = Format.R8G8B8A8Srgb; // pick one and use it everywhere
            ImageCreateInfo imgInfo = new()
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = offFormat,
                Extent = new Extent3D(_offscreenExtent.Width, _offscreenExtent.Height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.ImageUsageColorAttachmentBit | ImageUsageFlags.ImageUsageTransferSrcBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };
            _vk.CreateImage(_device, &imgInfo, null, out _offscreenImage);
            _vk.GetImageMemoryRequirements(_device, _offscreenImage, out var reqs);
            uint memType = FindMemoryType(reqs.MemoryTypeBits, MemoryPropertyFlags.MemoryPropertyDeviceLocalBit, in _memProps);
            MemoryAllocateInfo mAI = new() { SType = StructureType.MemoryAllocateInfo, AllocationSize = reqs.Size, MemoryTypeIndex = memType };
            _vk.AllocateMemory(_device, &mAI, null, out _offscreenMemory);
            _vk.BindImageMemory(_device, _offscreenImage, _offscreenMemory, 0);

            ImageViewCreateInfo viewCI = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _offscreenImage,
                ViewType = ImageViewType.Type2D,
                Format = offFormat,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            _vk.CreateImageView(_device, &viewCI, null, out _offscreenView);

            // --- Off-screen render pass & framebuffer ---
            var att = new AttachmentDescription(
                format: offFormat,
                samples: SampleCountFlags.SampleCount1Bit,
                loadOp: AttachmentLoadOp.Clear,
                storeOp: AttachmentStoreOp.Store,
                stencilLoadOp: AttachmentLoadOp.DontCare,
                stencilStoreOp: AttachmentStoreOp.DontCare,
                initialLayout: ImageLayout.Undefined,
                finalLayout: ImageLayout.ColorAttachmentOptimal
            );
            var attRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
            var sp = new SubpassDescription(pipelineBindPoint: PipelineBindPoint.Graphics, colorAttachmentCount: 1, pColorAttachments: &attRef);
            var dep = new SubpassDependency(
                srcSubpass: Vk.SubpassExternal, dstSubpass: 0,
                srcStageMask: PipelineStageFlags.ColorAttachmentOutputBit,
                dstStageMask: PipelineStageFlags.ColorAttachmentOutputBit,
                srcAccessMask: 0,
                dstAccessMask: AccessFlags.AccessColorAttachmentWriteBit
            );
            var rpCI = new RenderPassCreateInfo(attachmentCount: 1, pAttachments: &att, subpassCount: 1, pSubpasses: &sp, dependencyCount: 1, pDependencies: &dep);
            _vk.CreateRenderPass(_device, rpCI, null, out _offscreenRenderPass);

            fixed (ImageView* pView = &_offscreenView)
            {
                FramebufferCreateInfo fbCI = new()
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = _offscreenRenderPass,
                    AttachmentCount = 1,
                    PAttachments = pView,
                    Width = _offscreenExtent.Width,
                    Height = _offscreenExtent.Height,
                    Layers = 1
                };
                _vk.CreateFramebuffer(_device, &fbCI, null, out _offscreenFramebuffer);
            }

            // --- Staging buffer for readback ---
            _stagingRing = new StagingRing(_vk, _device, _memProps, _graphicsQueueIndex, _imageSize);

            // --- Shaders, pipeline layout, pipeline (use _offscreenRenderPass) ---
            using var stream = FileSystem.OpenAppPackageFileAsync("simple.vert.spv").Result;
            using var streamReader = new BinaryReader(stream);
            var vertBytes = streamReader.ReadBytes(Int32.MaxValue / 2);


            using var kaleidoscopeStream = FileSystem.OpenAppPackageFileAsync("kaleidoscope.frag.spv").Result;
            using var kaleidoscopeStreamReader = new BinaryReader(kaleidoscopeStream);
            var kaleidoscopeBytes = kaleidoscopeStreamReader.ReadBytes(Int32.MaxValue / 2);

            using var geometryStream = FileSystem.OpenAppPackageFileAsync("geometry.frag.spv").Result;
            using var geometryStreamReader = new BinaryReader(geometryStream);
            var geometryBytes = geometryStreamReader.ReadBytes(Int32.MaxValue / 2);

            _vertShaderModule = CreateShaderModule(vertBytes);
            _kaleidoscopeShaderModule = CreateShaderModule(kaleidoscopeBytes);
            _geometryShaderModule = CreateShaderModule(geometryBytes);

            var vp = new Viewport(0, 0, _offscreenExtent.Width, _offscreenExtent.Height, 0f, 1f);
            var sc = new Rect2D(new Offset2D(0, 0), _offscreenExtent);
            var vpState = new PipelineViewportStateCreateInfo(viewportCount: 1, pViewports: &vp, scissorCount: 1, pScissors: &sc);

            var pushRange = new PushConstantRange(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<PushData>());
            var plCI = new PipelineLayoutCreateInfo(pushConstantRangeCount: 1, pPushConstantRanges: &pushRange);
            _vk.CreatePipelineLayout(_device, plCI, null, out _pipelineLayout);

            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo(stage: ShaderStageFlags.VertexBit, module: _vertShaderModule, pName: (byte*)SilkMarshal.StringToPtr("main"));
            if (_shader == Shader.Kaleidoscope)
            {
                shaderStages[1] = new PipelineShaderStageCreateInfo(stage: ShaderStageFlags.FragmentBit, module: _kaleidoscopeShaderModule, pName: (byte*)SilkMarshal.StringToPtr("main"));
            }
            else
            {
                shaderStages[1] = new PipelineShaderStageCreateInfo(stage: ShaderStageFlags.FragmentBit, module: _geometryShaderModule, pName: (byte*)SilkMarshal.StringToPtr("main"));
            }
            

            var vi = new PipelineVertexInputStateCreateInfo(StructureType.PipelineVertexInputStateCreateInfo);
            var ia = new PipelineInputAssemblyStateCreateInfo(topology: PrimitiveTopology.TriangleList);
            var rs = new PipelineRasterizationStateCreateInfo(polygonMode: PolygonMode.Fill, cullMode: CullModeFlags.CullModeNone, frontFace: FrontFace.CounterClockwise, lineWidth: 1f);
            var ms = new PipelineMultisampleStateCreateInfo(rasterizationSamples: SampleCountFlags.SampleCount1Bit);
            var blendAtt = new PipelineColorBlendAttachmentState(blendEnable: false,
                colorWriteMask: ColorComponentFlags.ColorComponentRBit | ColorComponentFlags.ColorComponentGBit |
                                ColorComponentFlags.ColorComponentBBit | ColorComponentFlags.ColorComponentABit);
            var blend = new PipelineColorBlendStateCreateInfo(attachmentCount: 1, pAttachments: &blendAtt);

            var gpCI = new GraphicsPipelineCreateInfo(
                stageCount: 2, pStages: shaderStages,
                pVertexInputState: &vi, pInputAssemblyState: &ia,
                pViewportState: &vpState, pRasterizationState: &rs,
                pMultisampleState: &ms, pDepthStencilState: null,
                pColorBlendState: &blend, pDynamicState: null,
                layout: _pipelineLayout, renderPass: _offscreenRenderPass, subpass: 0
            );
            _vk.CreateGraphicsPipelines(_device, default, 1, gpCI, null, out _pipeline);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static unsafe Bool32 DebugCallback(
            DebugUtilsMessageSeverityFlagsEXT severity,
            DebugUtilsMessageTypeFlagsEXT types,
            DebugUtilsMessengerCallbackDataEXT* callbackData,
            void* userData)
        {
            string msg = SilkMarshal.PtrToString((nint)callbackData->PMessage);
            Console.WriteLine($"[Vulkan] {severity}: {msg}");
            System.Diagnostics.Debug.Print($"[Vulkan] {severity}: {msg}");
            return Vk.False; // or new Bool32(0)
        }

        private ExtDebugUtils _debugUtils;
        private DebugUtilsMessengerEXT _debugMessenger;

        private unsafe void SetupDebugMessenger()
        {
            _debugUtils = new ExtDebugUtils(_vk.Context);

            var createInfo = new DebugUtilsMessengerCreateInfoEXT
            {
                SType = StructureType.DebugUtilsMessengerCreateInfoExt,
                MessageSeverity =
                    DebugUtilsMessageSeverityFlagsEXT.DebugUtilsMessageSeverityVerboseBitExt |
                    DebugUtilsMessageSeverityFlagsEXT.DebugUtilsMessageSeverityWarningBitExt |
                    DebugUtilsMessageSeverityFlagsEXT.DebugUtilsMessageSeverityErrorBitExt,
                MessageType =
                    DebugUtilsMessageTypeFlagsEXT.DebugUtilsMessageTypeGeneralBitExt |
                    DebugUtilsMessageTypeFlagsEXT.DebugUtilsMessageTypeValidationBitExt |
                    DebugUtilsMessageTypeFlagsEXT.DebugUtilsMessageTypePerformanceBitExt,
                PfnUserCallback = (delegate* unmanaged[Cdecl]
                    <DebugUtilsMessageSeverityFlagsEXT,
                     DebugUtilsMessageTypeFlagsEXT,
                     DebugUtilsMessengerCallbackDataEXT*,
                     void*, Bool32>)&DebugCallback
            };

            if (_debugUtils.CreateDebugUtilsMessenger(_instance, in createInfo, null, out _debugMessenger) != Result.Success)
                throw new Exception("Failed to create debug messenger");
        }

        private uint FindMemoryType(
            uint typeFilter,
            MemoryPropertyFlags properties,
            in PhysicalDeviceMemoryProperties memProps)
        {
            for (uint i = 0; i < memProps.MemoryTypeCount; i++)
            {
                bool typeSupported = (typeFilter & (1u << (int)i)) != 0;
                bool hasProps = (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties;

                if (typeSupported && hasProps)
                    return i;
            }

            throw new Exception("Failed to find suitable memory type!");
        }

        private unsafe void BeginSwapchainRecreate()
        {
            _recreatingSwapchain = true;     // stop OnRender from submitting
            _imageAcquired = false;          // we won’t reuse any previously acquired image
            _vk.QueueWaitIdle(_graphicsQueue); // <<< CRITICAL: drain all in-flight work/presents
                                               // You can use _vk.DeviceWaitIdle(_device) if you prefer to be extra safe.
        }

        private void EndSwapchainRecreate()
        {
            _recreatingSwapchain = false;
        }

        private unsafe void OnRender(double obj)
        {
            if (_recreatingSwapchain || _isClosing || _tearingDownWSI)
            {
                return;
            }
            // Skip presentation if resize is pending (swapchain size doesn't match window)
            var present = PRESENT && !_resizePending;
            try
            {
            // Decide ring slot we're writing this frame:
            // If ring is full, we skip staging copy but still render and present
            int slot = _stagingRing.WriteIndex;
            bool canDoStagingCopy = true;
            if (Volatile.Read(ref _slotReadyGpuValue[slot]) != 0)
            {
                int free = -1;
                for (int i = 0; i < StagingRing.RingSize; i++)
                {
                    int s = (slot + i) % StagingRing.RingSize;
                    if (Volatile.Read(ref _slotReadyGpuValue[s]) == 0) { free = s; break; }
                }
                if (free < 0)
                {
                    // Ring full - skip staging copy but continue with render and presentation
                    canDoStagingCopy = false;
                }
                else
                {
                    _stagingRing.WriteIndex = free;
                    slot = free;
                }
            }

            var cmd = _cmds[_currentFrame];

            // If we're presenting, acquire a swapchain image early
            uint imageIndex = 0;
            if (PRESENT && !_isClosing)
            {
                // Bail if swapchain not ready
                if (_swapchain.Handle == 0 || _swapchainImages == null || _swapchainImages.Length == 0)
                    return;

                // If we don’t already own an image, try to acquire one with finite timeout
                if (!_imageAcquired)
                {
                    var acq = _swapchainExtension.AcquireNextImage(
                        _device, _swapchain,
                        0ul, // <-- finite timeout: non-blocking
                        _imageAvailableSemaphores[_currentFrame],
                        default,
                        ref imageIndex);

                    if (acq == Result.NotReady)
                    {
                        // No image available right now. Don’t reset the fence, just try again next frame.
                        return;
                    }
                    if (acq == Result.ErrorOutOfDateKhr && !_isClosing)
                    {
                        // Trigger your swapchain recreate path here
                        BeginSwapchainRecreate();
#if WINDOWS
                        _graphicsQueueIndex = CreateOrRecreateSwapchainPinned(_presentWindow);
#endif
                        EndSwapchainRecreate();
                        return;
                    }
                    // Success or Suboptimal
                    _imageAcquired = true;
                    _acquiredImageIndex = imageIndex;

                    if (_imageSerial[imageIndex] != 0)
                    {
                        var wi = new SemaphoreWaitInfo { SType = StructureType.SemaphoreWaitInfo, SemaphoreCount = 1 };
                        fixed (Semaphore* p = &_gpuDoneTL)
                        {
                            fixed(ulong* imageSerial = &_imageSerial[imageIndex])
                            {
                                wi.PSemaphores = p;
                                wi.PValues = imageSerial;
                                _vk.WaitSemaphores(_device, &wi, ulong.MaxValue);
                            }
                        }
                    }
                }
                else
                {
                    // Reuse the one we already acquired previously (e.g., last frame aborted before present)
                    imageIndex = _acquiredImageIndex;
                }
            }

            // 1) Ensure last submit finished before touching this CB
            _vk.WaitForFences(_device, 1, in _submitFence, true, ulong.MaxValue);

            // Record commands
            _vk.ResetCommandBuffer(cmd, 0);

            CommandBufferBeginInfo begin = new() { SType = StructureType.CommandBufferBeginInfo };
            _vk.BeginCommandBuffer(cmd, &begin);

            // --- Off-screen render pass ---
            var clear = new ClearValue(new ClearColorValue(0.1f, 0.1f, 0.1f, 1f));
            RenderPassBeginInfo rpBegin = new()
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _offscreenRenderPass,
                Framebuffer = _offscreenFramebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), _offscreenExtent),
                ClearValueCount = 1,
                PClearValues = &clear
            };

            _vk.CmdBeginRenderPass(cmd, &rpBegin, SubpassContents.Inline);
            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);

            PushData pc = new()
            {
                Resolution = new System.Numerics.Vector2(_offscreenExtent.Width, _offscreenExtent.Height),
                Time = (float)_stopwatch.Elapsed.TotalSeconds * 0.25f,
                DistortionStrength = 0.12f,
                HueTolerance = 0.35f,
                Falloff = 0.6f,
                ColorCount = Colors.Length
            };
            var colors = Colors;
            for (int i = 0; i < colors.Length && i < 6; i++)
            {
                pc.Colors[i] = new Vector4(colors[i].R, colors[i].G, colors[i].B, 0.0f);
            }
            _vk.CmdPushConstants(cmd, _pipelineLayout, ShaderStageFlags.FragmentBit,
                0, (uint)Unsafe.SizeOf<PushData>(), &pc);

            _vk.CmdDraw(cmd, 3, 1, 0, 0);
            _vk.CmdEndRenderPass(cmd);

            // Transition off-screen image to TRANSFER_SRC for copy/blit
            ImageMemoryBarrier toSrc = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.ColorAttachmentOptimal,
                NewLayout = ImageLayout.TransferSrcOptimal,
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit,
                Image = _offscreenImage,
                SubresourceRange = new ImageSubresourceRange(
                    ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            _vk.CmdPipelineBarrier(
                cmd,
                PipelineStageFlags.ColorAttachmentOutputBit,
                PipelineStageFlags.TransferBit,
                0, 0, null, 0, null, 1, &toSrc
            );

            // Copy off-screen -> staging buffer for CPU readback (if slot available)
            if (canDoStagingCopy)
            {
                BufferImageCopy region = new()
                {
                    BufferOffset = 0,
                    BufferRowLength = 0,
                    BufferImageHeight = 0,
                    ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    ImageOffset = new Offset3D(0, 0, 0),
                    ImageExtent = new Extent3D(_offscreenExtent.Width, _offscreenExtent.Height, 1),
                };
                Buffer dst = _stagingRing.CurrentWriteBuffer;
                _vk.CmdCopyImageToBuffer(cmd, _offscreenImage, ImageLayout.TransferSrcOptimal, dst, 1, &region);
            }

            // If we're presenting, blit the off-screen image into the swapchain image
            if (present)
            {
                if (_swapExtent.Width == 0 || _swapExtent.Height == 0)
                {
                    // Skip frame will resizing
                    return;
                }

                // swapchain image: UNDEFINED -> TRANSFER_DST
                ImageMemoryBarrier swapToDst = new()
                {
                    SType = StructureType.ImageMemoryBarrier,
                    OldLayout = ImageLayout.Undefined,
                    NewLayout = ImageLayout.TransferDstOptimal,
                    SrcAccessMask = 0,
                    DstAccessMask = AccessFlags.TransferWriteBit,
                    Image = _swapchainImages[imageIndex],
                    SubresourceRange = new ImageSubresourceRange(
                        ImageAspectFlags.ColorBit, 0, 1, 0, 1)
                };
                _vk.CmdPipelineBarrier(
                    cmd,
                    PipelineStageFlags.TopOfPipeBit,
                    PipelineStageFlags.TransferBit,
                    0, 0, null, 0, null, 1, &swapToDst
                );

                ImageBlit blit = new()
                {
                    SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    SrcOffsets = new ImageBlit.SrcOffsetsBuffer
                    {
                        Element0 = new(0, 0, 0),
                        Element1 = new((int)_offscreenExtent.Width, (int)_offscreenExtent.Height, 1)
                    },
                    DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    DstOffsets = new ImageBlit.DstOffsetsBuffer
                    {
                        Element0 = new(0, 0, 0),
                        Element1 = new((int)_swapExtent.Width, (int)_swapExtent.Height, 1)
                    }
                };

                _vk.CmdBlitImage(
                    cmd,
                    _offscreenImage, ImageLayout.TransferSrcOptimal,
                    _swapchainImages[imageIndex], ImageLayout.TransferDstOptimal,
                    1, &blit, Filter.Linear
                );

                // TRANSFER_DST -> PRESENT_SRC_KHR
                ImageMemoryBarrier toPresent = swapToDst;
                toPresent.OldLayout = ImageLayout.TransferDstOptimal;
                toPresent.NewLayout = ImageLayout.PresentSrcKhr;
                toPresent.SrcAccessMask = AccessFlags.TransferWriteBit;
                toPresent.DstAccessMask = 0;

                _vk.CmdPipelineBarrier(
                    cmd,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.BottomOfPipeBit,
                    0, 0, null, 0, null, 1, &toPresent
                );
            }

                ImageMemoryBarrier backToColor = new()
                {
                    SType = StructureType.ImageMemoryBarrier,
                    OldLayout = ImageLayout.TransferSrcOptimal,
                    NewLayout = ImageLayout.ColorAttachmentOptimal,
                    SrcAccessMask = AccessFlags.TransferReadBit,
                    DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
                    Image = _offscreenImage,
                    SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
                };
                _vk.CmdPipelineBarrier(
                    cmd,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.ColorAttachmentOutputBit,
                    0, 0, null, 0, null, 1, &backToColor
                );

                _vk.EndCommandBuffer(cmd);

            ulong frameValue = (ulong)Interlocked.Increment(ref _timelineValue);

            // Only wait for CPU release if we're actually using a staging slot
            ulong requiredRelease = canDoStagingCopy ? Volatile.Read(ref _slotNeedCpuRelease[slot]) : 0;
            bool needWait = requiredRelease != 0;

            // --- assemble waits in a single consistent order ---
            int waitCount = 0;
            var waitSems = stackalloc Semaphore[2];            // cpuDoneTL, imageAvailable (if present)
            var waitStages = stackalloc PipelineStageFlags[2];
            var waitVals = stackalloc ulong[2];                // MUST be same length as waitSems

            if (needWait)
            {
                waitSems[waitCount] = _cpuDoneTL;             // timeline
                waitStages[waitCount] = PipelineStageFlags.PipelineStageTopOfPipeBit;
                waitVals[waitCount] = requiredRelease;        // <- value for timeline wait
                waitCount++;
            }
            if (present)
            {
                waitSems[waitCount] = _imageAvailableSemaphores[_currentFrame]; // binary
                waitStages[waitCount] = PipelineStageFlags.PipelineStageTransferBit;
                waitVals[waitCount] = 0;                      // <- dummy, ignored for binary
                waitCount++;
            }

            // --- assemble signals in a single consistent order ---
            int sigCount = 0;
            var sigSems = stackalloc Semaphore[2];               // gpuDoneTL, renderFinished (if present)
            var sigVals = stackalloc ulong[2];

            ulong producedValue = ++_gpuCtr;
            sigSems[sigCount] = _gpuDoneTL;                      // timeline
            sigVals[sigCount] = producedValue;                   // <- timeline value
            sigCount++;

            if (present)
            {
                sigSems[sigCount] = _renderFinishedByImage[imageIndex]; // binary
                sigVals[sigCount] = 0;                          // <- dummy, ignored for binary
                sigCount++;
            }

            TimelineSemaphoreSubmitInfo tl = new()
            {
                SType = StructureType.TimelineSemaphoreSubmitInfo,
                WaitSemaphoreValueCount = (uint)waitCount,
                PWaitSemaphoreValues = waitCount > 0 ? waitVals : null,
                SignalSemaphoreValueCount = (uint)sigCount,
                PSignalSemaphoreValues = sigCount > 0 ? sigVals : null
            };

            var submitInfo = new SubmitInfo(
                waitSemaphoreCount: (uint)waitCount,
                pWaitSemaphores: waitCount > 0 ? waitSems : null,
                pWaitDstStageMask: waitCount > 0 ? waitStages : null,
                commandBufferCount: 1,
                pCommandBuffers: &cmd,
                signalSemaphoreCount: (uint)sigCount,
                pSignalSemaphores: sigSems
            );
            submitInfo.PNext = &tl;


            // Check again before submit - swapchain may have been destroyed during command buffer recording
            if (present && (_recreatingSwapchain || _swapchain.Handle == 0))
            {
                _imageAcquired = false;
                return;
            }

            _vk.ResetFences(_device, 1, in _submitFence);

            // Submit
            if (present)
            {
                var renderFinishedForThisImage = _renderFinishedByImage[imageIndex];
                var waitStage = PipelineStageFlags.PipelineStageTransferBit; // we first use the swapchain image in a transfer op
                fixed (Semaphore* pWait = &_imageAvailableSemaphores[_currentFrame])
                {
                    Semaphore* pSignal = &renderFinishedForThisImage;

                    CommandBuffer* pCmd = &cmd;
                    ulong val;
                    var res = _vk.QueueSubmit(_graphicsQueue, 1, ref submitInfo, _submitFence);
                    if (res != Result.Success)
                    {
                        System.Diagnostics.Debug.Print($"QueueSubmit failed: {res}");
                        throw new Exception($"QueueSubmit failed: {res}");
                    }

                    // Present waits on renderFinished
                    fixed (SwapchainKHR* pSwap = &_swapchain)
                    {
                        var presentInfo = new PresentInfoKHR(
                            waitSemaphoreCount: 1, pWaitSemaphores: pSignal,
                            swapchainCount: 1, pSwapchains: pSwap,
                            pImageIndices: &imageIndex
                        );
                        var pres = _swapchainExtension.QueuePresent(_graphicsQueue, presentInfo);
                        _imageAcquired = false;
                        if ((pres == Result.ErrorOutOfDateKhr || pres == Result.SuboptimalKhr) && !_isClosing)
                        {
#if WINDOWS
                            _graphicsQueueIndex = CreateOrRecreateSwapchainPinned(_presentWindow);
#endif
                            return;
                        }
                    }
                    _imageSerial[imageIndex] = producedValue;
                }
            }
            else
            {


                // No presentation path: just submit the off-screen work
                var res = _vk.QueueSubmit(_graphicsQueue, 1, ref submitInfo, _submitFence);
                if (res != Result.Success)
                {
                    System.Diagnostics.Debug.Print($"QueueSubmit failed: {res}");
                    throw new Exception($"QueueSubmit failed: {res}");
                }
            }

            // Only update slot tracking if we actually used a staging slot
            if (canDoStagingCopy)
            {
                Volatile.Write(ref _slotReadyGpuValue[slot], producedValue);

                // and record that this is the value that must be released before next reuse:
                Volatile.Write(ref _slotNeedCpuRelease[slot], producedValue);

                _slotSignaledValue[_stagingRing.WriteIndex] = frameValue;
                _stagingRing.AdvanceWrite();
            }

            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;

            }
            catch
            {

            }
        }

        public unsafe RenderedImageHandle<byte> ReadFromMemory()
        {
            _readReadyEvent.Wait();

            if (_stagingRing == null || _stagingRing.IsDisposed)
            {
                return RenderedImageHandle<byte>.Empty;
            }

            int slot = _stagingRing.ReadIndex;
            ulong produced = Volatile.Read(ref _slotReadyGpuValue[slot]);
            if (produced == 0)
            {
                for (int i = 1; i < StagingRing.RingSize; i++)
                {
                    int s = (slot + i) % StagingRing.RingSize;
                    ulong p = Volatile.Read(ref _slotReadyGpuValue[s]);
                    if (p != 0) { _stagingRing.ReadIndex = s; slot = s; produced = p; break; }
                }
                if (produced == 0)
                    return RenderedImageHandle<byte>.Empty;
            }

            // (1) Wait for GPU to produce this slot
            SemaphoreWaitInfo wi = new() { SType = StructureType.SemaphoreWaitInfo, SemaphoreCount = 1 };
            fixed (Semaphore* pSem = &_gpuDoneTL)
            {
                wi.PSemaphores = pSem;
                wi.PValues = &produced;
                _vk.WaitSemaphores(_device, &wi, ulong.MaxValue);
            }
            Interlocked.Increment(ref _pendingMemoryReads);

            var memory = _stagingRing.CurrentReadMemory;
            var releaseAction = (int slotValue, ulong producedValue) =>
            {
                try
                {
                    // (3) Signal CPU release - only if this value is higher than what we've signaled
                    // Timeline semaphores require monotonically increasing values
                    ulong current, newValue;
                    do
                    {
                        current = Volatile.Read(ref _cpuSignaled);
                        if (producedValue <= current)
                        {
                            // Already signaled past this value, skip signaling
                            break;
                        }
                        newValue = producedValue;
                    } while (Interlocked.CompareExchange(ref _cpuSignaled, newValue, current) != current);

                    if (producedValue > current)
                    {
                        SemaphoreSignalInfo sig = new() { SType = StructureType.SemaphoreSignalInfo, Semaphore = _cpuDoneTL, Value = producedValue };
                        _vk.SignalSemaphore(_device, &sig);
                    }

                    // (4) Clear and advance
                    Volatile.Write(ref _slotReadyGpuValue[slotValue], 0);
                    _stagingRing.AdvanceRead();
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingMemoryReads);
                }
            };
            return new RenderedImageHandle<byte>(memory, releaseAction, slot, produced);
        }

        public RenderedImageHandle<byte> GetRenderedImage()
        {
            return ReadFromMemory();
        }

        private unsafe ShaderModule CreateShaderModule(byte[] code)
        {
            fixed (byte* codePtr = code)
            {
                var createInfo = new ShaderModuleCreateInfo(
                    codeSize: (nuint)code.Length,
                    pCode: (uint*)codePtr
                );

                if (_vk.CreateShaderModule(_device, createInfo, null, out var shaderModule) != Result.Success)
                {
                    throw new Exception("Failed");
                }
                return shaderModule;
            }
        }

        private unsafe void RecreateGraphicsPipeline()
        {
            // Destroy old pipeline(s)
            if (_pipeline.Handle != 0)
            {
                _vk.DestroyPipeline(_device, _pipeline, null);
                _pipeline = default;
            }

            // Optionally destroy/recreate only the fragment module and keep vertex module
            if (_kaleidoscopeShaderModule.Handle != 0 && _shader != Shader.Kaleidoscope)
            {
                _vk.DestroyShaderModule(_device, _kaleidoscopeShaderModule, null);
                _kaleidoscopeShaderModule = default;
            }
            if (_geometryShaderModule.Handle != 0 && _shader != Shader.Geometry)
            {
                _vk.DestroyShaderModule(_device, _geometryShaderModule, null);
                _geometryShaderModule = default;
            }

            // Ensure the selected frag module exists (reload bytes if needed)
            var fragModule = _shader == Shader.Kaleidoscope ? _kaleidoscopeShaderModule : _geometryShaderModule;
            if (fragModule.Handle == 0)
            {
                using var stream = FileSystem.OpenAppPackageFileAsync($"{_shader.ToString().ToLower()}.frag.spv").Result;
                using var streamReader = new BinaryReader(stream);
                var bytes = streamReader.ReadBytes(Int32.MaxValue / 2);

                fragModule = CreateShaderModule(bytes);
                if (_shader == Shader.Kaleidoscope) _kaleidoscopeShaderModule = fragModule;
                else _geometryShaderModule = fragModule;
            }

            // Rebuild pipeline with _pipelineLayout and _offscreenRenderPass
            var vp = new Viewport(0, 0, _offscreenExtent.Width, _offscreenExtent.Height, 0f, 1f);
            var sc = new Rect2D(new Offset2D(0, 0), _offscreenExtent);
            var vpState = new PipelineViewportStateCreateInfo(viewportCount: 1, pViewports: &vp, scissorCount: 1, pScissors: &sc);

            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo(stage: ShaderStageFlags.VertexBit, module: _vertShaderModule, pName: (byte*)SilkMarshal.StringToPtr("main"));
            stages[1] = new PipelineShaderStageCreateInfo(stage: ShaderStageFlags.FragmentBit, module: fragModule, pName: (byte*)SilkMarshal.StringToPtr("main"));

            var vi = new PipelineVertexInputStateCreateInfo() { SType = StructureType.PipelineVertexInputStateCreateInfo };
            var ia = new PipelineInputAssemblyStateCreateInfo(topology: PrimitiveTopology.TriangleList);
            var rs = new PipelineRasterizationStateCreateInfo(polygonMode: PolygonMode.Fill, cullMode: CullModeFlags.CullModeNone, frontFace: FrontFace.CounterClockwise, lineWidth: 1f);
            var ms = new PipelineMultisampleStateCreateInfo(rasterizationSamples: SampleCountFlags.Count1Bit);
            var blendAtt = new PipelineColorBlendAttachmentState(blendEnable: false,
                colorWriteMask: ColorComponentFlags.ColorComponentRBit | ColorComponentFlags.ColorComponentGBit |
                                ColorComponentFlags.ColorComponentBBit | ColorComponentFlags.ColorComponentABit);
            var blend = new PipelineColorBlendStateCreateInfo(attachmentCount: 1, pAttachments: &blendAtt);

            var gpCI = new GraphicsPipelineCreateInfo(
                stageCount: 2, pStages: stages,
                pVertexInputState: &vi, pInputAssemblyState: &ia,
                pViewportState: &vpState, pRasterizationState: &rs,
                pMultisampleState: &ms, pDepthStencilState: null,
                pColorBlendState: &blend, pDynamicState: null,
                layout: _pipelineLayout, renderPass: _offscreenRenderPass, subpass: 0
            );

            _vk.CreateGraphicsPipelines(_device, default, 1, gpCI, null, out _pipeline);
            _stagingRing = new StagingRing(_vk, _device, _memProps, _graphicsQueueIndex, _imageSize);
        }

        public void SetShader(Shader shader)
        {
            // 1) Tell render loop to pause submits
            _readReadyEvent.Reset();              // stop new ReadFromMemory users
            _running = false;                     // ask render loop to exit its while-step boundary
            var rt = _renderThread;
            if (rt != null && rt.IsAlive && Thread.CurrentThread != rt)
                rt.Join();                        // wait for render loop to be fully quiescent

            _vk.WaitForFences(_device, 1, in _submitFence, true, ulong.MaxValue);

            // 2) Drain GPU once (no WSI teardown)
            _vk.QueueWaitIdle(_graphicsQueue);

            // Bring _cpuDoneTL up to match _gpuCtr so no stale waits remain
            if (_gpuCtr > _cpuSignaled)
            {
                unsafe
                {
                    SemaphoreSignalInfo sig = new()
                    {
                        SType = StructureType.SemaphoreSignalInfo,
                        Semaphore = _cpuDoneTL,
                        Value = _gpuCtr
                    };
                    _vk.SignalSemaphore(_device, &sig);
                    _cpuSignaled = _gpuCtr;
                }
            }

            // Clear slot tracking to prevent stale values from blocking new staging ring
            for (int i = 0; i < StagingRing.RingSize; i++)
            {
                _slotReadyGpuValue[i] = 0;
                _slotNeedCpuRelease[i] = 0;
            }

            _stagingRing?.Dispose();
            _stagingRing = null;

            // 3) Rebuild pipeline with the new fragment shader
            _shader = shader;
            RecreateGraphicsPipeline();           // destroy old _pipeline + chosen frag module, create new

            // 4) Resume rendering
            _running = true;
            _renderThread = new Thread(RenderLoop) { IsBackground = true };
            _renderThread.Start();
            _readReadyEvent.Set();
        }
    }
}