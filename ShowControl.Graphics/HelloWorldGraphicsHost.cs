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
using Silk.NET.Maths;
using IWindow = Silk.NET.Windowing.IWindow;
using Image = Silk.NET.Vulkan.Image;




#if !ARM64
using Silk.NET.Windowing;
#endif

namespace ShowControl.Graphics
{
    public class HelloWorldGraphicsHost
    {

        public HelloWorldGraphicsHost()
        {
            int x = 0;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct PushData
        {
            public Vector2 Resolution;  // width, height
            public float Time;
            private float _pad;         // keep total size multiple of 16
        }

#if !ARM64
        private static Silk.NET.Windowing.IWindow _window;
        private static Silk.NET.Windowing.IWindow _presentWindow;
#endif
        private static Vk _vk;
        private static Instance _instance;
        private static SurfaceKHR _surface;
        private static KhrSurface _khrSurface;
        #if !ARM64
        private static KhrWin32Surface _khrWin32Surface;
        #endif
        private static PhysicalDevice _physicalDevice;
        private static Device _device;
        private static Queue _graphicsQueue;
        private static KhrSwapchain _khrSwapchain;
        private static SwapchainKHR _swapchain;
        private static Silk.NET.Vulkan.Image[] _swapchainImages;
        private static ImageView[] _swapchainImageViews;

        private static CommandPool _commandPool;
        private static CommandBuffer[] _commandBuffers;

        private static RenderPass _renderPass;
        private static RenderPass _offscreenRenderPass;
        private static Framebuffer[] _framebuffers;

        private static KhrSwapchain _swapchainExtension;
        private static Pipeline _pipeline;
        private static PipelineLayout _pipelineLayout;

        private static Fence[] _inFlightFences;
        private static Semaphore[] _imageAvailableSemaphores;
        private static Semaphore[] _renderFinishedSemaphores;
        private static int _currentFrame = 0;

        private ShaderModule _vertShaderModule;
        private ShaderModule _fragShaderModule;

        private static PhysicalDeviceMemoryProperties _memProps;
        private static Extent2D _swapExtent;
        private static DeviceMemory _offscreenMemory;
        private static DeviceMemory _deviceMemory;
        private static Silk.NET.Vulkan.Image _offscreenImage;
        private static ImageView _offscreenView;
        private static Framebuffer _offscreenFramebuffer;
        private static ulong _imageSize;

        private static Silk.NET.Vulkan.Buffer _buffer;

        private static Stopwatch _stopwatch = new Stopwatch();

        const int MAX_FRAMES_IN_FLIGHT = 2;

        public static object _lock = new object();
        public static byte[] Bytes = null;

        private static bool PRESENT = false; // set false to run headless (no window present)
        private static Extent2D _offscreenExtent;
        private static CommandBuffer _cmd; // single reusable CB for off-screen + optional blit

        private volatile bool _running;
        private Thread _renderThread;

        public void Run()
        {
            try
            {
                //System.Threading.Thread.Sleep(20000);
                RunHeadless();
                #if !ARM64
                ShowHeadlessInWindow();
                #endif
            }
            catch (Exception e)
            {
                int x = 0;
            }
        }

        public void RunHeadless(int width = 1600, int height = 1200)
        {
            PRESENT = false; // make sure your OnRender uses the no-present path
            _offscreenExtent = new Extent2D((uint)width, (uint)height);
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
                OnRender(0);        // your existing OnRender with PRESENT=false
                Thread.Yield();     // or Thread.Sleep(0) / Sleep(1) if you want to throttle
            }
        }

        public void StopHeadless()
        {
            _running = false;
            _renderThread?.Join();

        }

#if !ARM64
        public unsafe void ShowHeadlessInWindow(int width = 1600, int height = 1200, string title = "Viewer")
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

                CreateOrRecreateSwapchainPinned(window);

                // Tell OnRender to execute the present path (Acquire->Blit->Present)
                PRESENT = true;

                // Optional: drive your render from the window loop instead of a background thread:
                // _running = false; // stop your headless loop if you prefer single-driver
            };

            // Use the window's Render event to call your OnRender (single driving loop),
            // or keep your background loop and just let OnRender see PRESENT=true.
            window.Render += OnRender;

            window.Run();
        }
        private unsafe static void EnablePresentation(IWindow window, Extent2D size, Format format, ColorSpaceKHR colorSpace)
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
            _swapchainImages = new Image[count];
            fixed (Image* p = _swapchainImages)
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, &count, p);
        }

        private static unsafe uint FindPresentQueueFamily(PhysicalDevice pd, SurfaceKHR surface)
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

        private static unsafe void CreateOrRecreateSwapchainPinned(IWindow window)
        {
            if (_surface.Handle == 0) throw new Exception("Surface not created. Call EnablePresentation first.");


            // --- Surface caps / formats / modes ---
            _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var caps);
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

            // Extent (some platforms force CurrentExtent)
            Extent2D extent;
            if (caps.CurrentExtent.Width != uint.MaxValue) extent = caps.CurrentExtent;
            else
            {
                var w = (uint)window.Size.X; var h = (uint)window.Size.Y;
                extent = new Extent2D(
                    Math.Clamp(w, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                    Math.Clamp(h, caps.MinImageExtent.Height, caps.MaxImageExtent.Height)
                );
            }

            // Image count
            uint imageCount = Math.Max(2, caps.MinImageCount);
            if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount) imageCount = caps.MaxImageCount;

            // Queue family setup
            uint gfxQ = 0;                // your function
            uint presentQ = FindPresentQueueFamily(_physicalDevice, _surface); // see helper below

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
                PreTransform = caps.CurrentTransform,
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
                if (_swapchain.Handle != 0) _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
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
                    if (_swapchain.Handle != 0) _swapchainExtension.DestroySwapchain(_device, _swapchain, null);
                    _swapchain = newSwap;
                } // <- pointer stays valid until after the call returns
            }

            // Fetch images
            uint count = 0;
            _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, null);
            _swapchainImages = new Image[count];
            fixed (Image* pImgs = _swapchainImages)
                _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref count, pImgs);

            _swapExtent = extent; // keep for blit
        }
#endif

        private static unsafe bool TryPickDeviceAndQueuesPreWindow(out PhysicalDevice picked, out uint gfxQ, out uint presentQ)
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
#if !ARM64
                    if (pIdx == uint.MaxValue && _khrWin32Surface.GetPhysicalDeviceWin32PresentationSupport(pd, i))
                        pIdx = i;
#else
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

        private static unsafe bool DeviceHasSwapchain(PhysicalDevice pd)
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
                apiVersion: Vk.Version11
            );

            // Instance extensions: none needed for pure headless.
            // On macOS/MoltenVK you likely need portability enumeration:
            var instExts = new List<string>();
#if ARM64
    //instExts.Add("VK_KHR_portability_enumeration");
    instExts.Add("VK_MVK_macos_surface");
#else
            instExts.Add("VK_KHR_win32_surface");
#endif
            instExts.Add("VK_KHR_surface");
            instExts.Add("VK_EXT_debug_utils");
            var instExtPtr = SilkMarshal.StringArrayToPtr(instExts.ToArray());
            var instFlags = InstanceCreateFlags.None;
            
            var layers = new List<string>();
#if ARM64
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
            #if !ARM64
            _khrWin32Surface = new KhrWin32Surface(_vk.Context);
            #endif

            // --- Physical device & queue family ---
            if (!TryPickDeviceAndQueuesPreWindow(out _physicalDevice, out uint graphicsQueue, out uint presentQueue))
                throw new Exception("No device has GRAPHICS and Win32 PRESENT support.");

            // --- Logical device ---
            float prio = 1f;
            var qCI = new DeviceQueueCreateInfo(queueFamilyIndex: graphicsQueue, queueCount: 1, pQueuePriorities: &prio);

            var devExts = new List<string>();
            devExts.Add("VK_KHR_swapchain");
#if ARM64
    // MoltenVK requires this device extension
    devExts.Add("VK_KHR_portability_subset");
#endif
            var devExtPtr = SilkMarshal.StringArrayToPtr(devExts.ToArray());

            var devCI = new DeviceCreateInfo(
                queueCreateInfoCount: 1,
                pQueueCreateInfos: &qCI,
                enabledExtensionCount: (uint)devExts.Count,
                ppEnabledExtensionNames: (byte**)devExtPtr
            );
            _vk.CreateDevice(_physicalDevice, devCI, null, out _device);
            _vk.GetDeviceQueue(_device, graphicsQueue, 0, out _graphicsQueue);

            _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out _memProps);

            // --- Sync objects (double-buffered fence & semaphores reused even if headless) ---
            _inFlightFences = new Fence[MAX_FRAMES_IN_FLIGHT];
            _imageAvailableSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            _renderFinishedSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            var fenceInfo = new FenceCreateInfo(flags: FenceCreateFlags.FenceCreateSignaledBit);
            var semInfo = new SemaphoreCreateInfo();
            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                _vk.CreateFence(_device, fenceInfo, null, out _inFlightFences[i]);
                _vk.CreateSemaphore(_device, semInfo, null, out _imageAvailableSemaphores[i]);
                _vk.CreateSemaphore(_device, semInfo, null, out _renderFinishedSemaphores[i]);
            }

            // --- Command pool & a single primary CB ---
            var poolCI = new CommandPoolCreateInfo(
                queueFamilyIndex: graphicsQueue,
                flags: CommandPoolCreateFlags.CommandPoolCreateResetCommandBufferBit
            );
            _vk.CreateCommandPool(_device, poolCI, null, out _commandPool);

            var allocOne = new CommandBufferAllocateInfo(
                commandPool: _commandPool,
                level: CommandBufferLevel.Primary,
                commandBufferCount: 1
            );
            _vk.AllocateCommandBuffers(_device, allocOne, out _cmd);

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
            Bytes = new byte[_imageSize];
            BufferCreateInfo bufCI = new() { SType = StructureType.BufferCreateInfo, Size = _imageSize, Usage = BufferUsageFlags.BufferUsageTransferDstBit, SharingMode = SharingMode.Exclusive };
            _vk.CreateBuffer(_device, &bufCI, null, out _buffer);
            _vk.GetBufferMemoryRequirements(_device, _buffer, out var bReqs);
            memType = FindMemoryType(bReqs.MemoryTypeBits, MemoryPropertyFlags.MemoryPropertyHostVisibleBit | MemoryPropertyFlags.MemoryPropertyHostCoherentBit, in _memProps);
            MemoryAllocateInfo bufAI = new() { SType = StructureType.MemoryAllocateInfo, AllocationSize = bReqs.Size, MemoryTypeIndex = memType };
            _vk.AllocateMemory(_device, &bufAI, null, out _deviceMemory);
            _vk.BindBufferMemory(_device, _buffer, _deviceMemory, 0);

            // --- Shaders, pipeline layout, pipeline (use _offscreenRenderPass) ---
            using var stream = FileSystem.OpenAppPackageFileAsync("simple.vert.spv").Result;
            using var streamReader = new BinaryReader(stream);
            var vertBytes = streamReader.ReadBytes(Int32.MaxValue / 2);


            using var stream2 = FileSystem.OpenAppPackageFileAsync("kaleidoscope.frag.spv").Result;
            using var streamReader2 = new BinaryReader(stream2);
            var fragBytes = streamReader2.ReadBytes(Int32.MaxValue / 2);
            _vertShaderModule = CreateShaderModule(vertBytes);
            _fragShaderModule = CreateShaderModule(fragBytes);

            var vp = new Viewport(0, 0, _offscreenExtent.Width, _offscreenExtent.Height, 0f, 1f);
            var sc = new Rect2D(new Offset2D(0, 0), _offscreenExtent);
            var vpState = new PipelineViewportStateCreateInfo(viewportCount: 1, pViewports: &vp, scissorCount: 1, pScissors: &sc);

            var pushRange = new PushConstantRange(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<PushData>());
            var plCI = new PipelineLayoutCreateInfo(pushConstantRangeCount: 1, pPushConstantRanges: &pushRange);
            _vk.CreatePipelineLayout(_device, plCI, null, out _pipelineLayout);

            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo(stage: ShaderStageFlags.VertexBit, module: _vertShaderModule, pName: (byte*)SilkMarshal.StringToPtr("main"));
            shaderStages[1] = new PipelineShaderStageCreateInfo(stage: ShaderStageFlags.FragmentBit, module: _fragShaderModule, pName: (byte*)SilkMarshal.StringToPtr("main"));

            var vi = new PipelineVertexInputStateCreateInfo();
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

        private static ExtDebugUtils _debugUtils;
        private static DebugUtilsMessengerEXT _debugMessenger;

        private static unsafe void SetupDebugMessenger()
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

        private static uint FindMemoryType(
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

        private static unsafe void OnRender(double obj)
        {
            // Wait & reset fence for this frame
            _vk.WaitForFences(_device, 1, in _inFlightFences[_currentFrame], true, ulong.MaxValue);
            _vk.ResetFences(_device, 1, in _inFlightFences[_currentFrame]);

            // If we're presenting, acquire a swapchain image early
            uint imageIndex = 0;
            if (PRESENT)
            {
                _swapchainExtension.AcquireNextImage(
                    _device, _swapchain, ulong.MaxValue,
                    _imageAvailableSemaphores[_currentFrame], default, ref imageIndex);
            }

            // Record commands
            _vk.ResetCommandBuffer(_cmd, 0);
            CommandBufferBeginInfo begin = new() { SType = StructureType.CommandBufferBeginInfo };
            _vk.BeginCommandBuffer(_cmd, &begin);

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

            _vk.CmdBeginRenderPass(_cmd, &rpBegin, SubpassContents.Inline);
            _vk.CmdBindPipeline(_cmd, PipelineBindPoint.Graphics, _pipeline);

            PushData pc = new()
            {
                Resolution = new System.Numerics.Vector2(_offscreenExtent.Width, _offscreenExtent.Height),
                Time = (float)_stopwatch.Elapsed.TotalSeconds
            };
            _vk.CmdPushConstants(_cmd, _pipelineLayout, ShaderStageFlags.FragmentBit,
                0, (uint)Unsafe.SizeOf<PushData>(), &pc);

            _vk.CmdDraw(_cmd, 3, 1, 0, 0);
            _vk.CmdEndRenderPass(_cmd);

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
                _cmd,
                PipelineStageFlags.ColorAttachmentOutputBit,
                PipelineStageFlags.TransferBit,
                0, 0, null, 0, null, 1, &toSrc
            );

            // Always copy off-screen -> staging buffer for CPU readback
            BufferImageCopy region = new()
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(_offscreenExtent.Width, _offscreenExtent.Height, 1),
            };
            _vk.CmdCopyImageToBuffer(_cmd, _offscreenImage, ImageLayout.TransferSrcOptimal, _buffer, 1, &region);

            // If we're presenting, blit the off-screen image into the swapchain image
            if (PRESENT)
            {
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
                    _cmd,
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
                        Element1 = new((int)_offscreenExtent.Width, (int)_offscreenExtent.Height, 1)
                    }
                };

                _vk.CmdBlitImage(
                    _cmd,
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
                    _cmd,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.BottomOfPipeBit,
                    0, 0, null, 0, null, 1, &toPresent
                );
            }

            _vk.EndCommandBuffer(_cmd);

            // Submit
            if (PRESENT)
            {
                var waitStage = PipelineStageFlags.PipelineStageTransferBit; // we first use the swapchain image in a transfer op
                fixed (Semaphore* pWait = &_imageAvailableSemaphores[_currentFrame])
                fixed (Semaphore* pSignal = &_renderFinishedSemaphores[_currentFrame])
                fixed (CommandBuffer* pCmd = &_cmd)
                {
                    var submitInfo = new SubmitInfo(
                        waitSemaphoreCount: 1, pWaitSemaphores: pWait,
                        pWaitDstStageMask: &waitStage,
                        commandBufferCount: 1, pCommandBuffers: pCmd,
                        signalSemaphoreCount: 1, pSignalSemaphores: pSignal
                    );
                    _vk.QueueSubmit(_graphicsQueue, 1, ref submitInfo, _inFlightFences[_currentFrame]);

                    // Present waits on renderFinished
                    fixed (SwapchainKHR* pSwap = &_swapchain)
                    {
                        var presentInfo = new PresentInfoKHR(
                            waitSemaphoreCount: 1, pWaitSemaphores: pSignal,
                            swapchainCount: 1, pSwapchains: pSwap,
                            pImageIndices: &imageIndex
                        );
                        _swapchainExtension.QueuePresent(_graphicsQueue, presentInfo);
                    }
                }
            }
            else
            {
                // No presentation path: just submit the off-screen work
                fixed (CommandBuffer* pCmd = &_cmd)
                {
                    var submitInfo = new SubmitInfo(
                        waitSemaphoreCount: 0, pWaitSemaphores: null,
                        pWaitDstStageMask: null,
                        commandBufferCount: 1, pCommandBuffers: pCmd,
                        signalSemaphoreCount: 0, pSignalSemaphores: null
                    );
                    _vk.QueueSubmit(_graphicsQueue, 1, ref submitInfo, _inFlightFences[_currentFrame]);
                }
            }

            // Wait for GPU, then map staging buffer and copy to Bytes[]
            _vk.WaitForFences(_device, 1, in _inFlightFences[_currentFrame], true, ulong.MaxValue);

            void* mapped;
            if (_vk.MapMemory(_device, _deviceMemory, 0, _imageSize, 0, &mapped) != Result.Success)
                throw new Exception("MapMemory failed");

            lock (_lock)
            {
                Marshal.Copy((IntPtr)mapped, Bytes, 0, (int)_imageSize);
            }
            _vk.UnmapMemory(_device, _deviceMemory);

            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        }

        private static unsafe ShaderModule CreateShaderModule(byte[] code)
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
    }
}