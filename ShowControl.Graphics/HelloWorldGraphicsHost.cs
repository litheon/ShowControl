using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.OpenAL;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Device = Silk.NET.Vulkan.Device;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace ShowControl.Graphics
{
    public class HelloWorldGraphicsHost
    {

        [StructLayout(LayoutKind.Sequential)]
        struct PushData
        {
            public Vector2 Resolution;  // width, height
            public float Time;
            private float _pad;         // keep total size multiple of 16
        }

        private static IWindow _window;
        private static Vk _vk;
        private static Instance _instance;
        private static SurfaceKHR _surface;
        private static KhrSurface _khrSurface;

        private static PhysicalDevice _physicalDevice;
        private static Device _device;
        private static Queue _graphicsQueue;
        private static KhrSwapchain _khrSwapchain;
        private static SwapchainKHR _swapchain;
        private static Image[] _swapchainImages;
        private static ImageView[] _swapchainImageViews;

        private static CommandPool _commandPool;
        private static CommandBuffer[] _commandBuffers;

        private static RenderPass _renderPass;
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
        private static Image _offscreenImage;
        private static ImageView _offscreenView;

        private static Silk.NET.Vulkan.Buffer _buffer;

        private static Stopwatch _stopwatch = new Stopwatch();

        const int MAX_FRAMES_IN_FLIGHT = 2;

        public void Run()
        {
            _stopwatch.Start();
            new Thread(() =>
            {
                var options = WindowOptions.DefaultVulkan;
                options.Size = new Vector2D<int>(800, 600);
                options.Title = "Silk.NET Vulkan Example";

                _window = Window.Create(options);
                _window.Load += OnLoad;
                _window.Render += OnRender;
                _window.Run();
            })
            { IsBackground = true }.Start();
        }

        private unsafe void OnLoad()
        {
            _vk = Vk.GetApi();

            _inFlightFences = new Fence[MAX_FRAMES_IN_FLIGHT];
            _imageAvailableSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
            _renderFinishedSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];

            // --- Instance ---
            var appInfo = new ApplicationInfo(
                pApplicationName: (byte*)SilkMarshal.StringToPtr("SilkVulkan"),
                applicationVersion: new Version32(1, 0, 0),
                pEngineName: (byte*)SilkMarshal.StringToPtr("NoEngine"),
                engineVersion: new Version32(1, 0, 0),
                apiVersion: Vk.Version11
            );

            var extensions = _window.VkSurface!.GetRequiredExtensions(out var count);
            var extensionsStringList = SilkMarshal.PtrToStringArray((nint)extensions, Convert.ToInt32(count)).ToList();
            //extensionsStringList.Add("VK_LAYER_KHRONOS_validation");
            var instanceExtensionPtrs = SilkMarshal.StringArrayToPtr(extensionsStringList.ToArray());

            var instanceCreateInfo = new InstanceCreateInfo(
                pApplicationInfo: &appInfo,
                enabledExtensionCount: Convert.ToUInt32(extensionsStringList.Count),
                ppEnabledExtensionNames: (byte**)instanceExtensionPtrs
            );

            var res = _vk.CreateInstance(instanceCreateInfo, null, out _instance);
            if (res != Result.Success)
                throw new Exception("Failed to create Vulkan instance.");

            //SilkMarshal.Free((nint)appInfo.PApplicationName);
            //SilkMarshal.Free((nint)appInfo.PEngineName);
            //for(uint i = 0; i < count; i++)
            //{
            //    SilkMarshal.Free((nint)extensions[i]);
            //}

            // Surface
            _khrSurface = new KhrSurface(_vk.Context);
            _surface = _window.VkSurface!.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();

            // --- Select Physical Device ---
            uint deviceCount = 0;
            _vk.EnumeratePhysicalDevices(_instance, ref deviceCount, null);
            var devices = new PhysicalDevice[deviceCount];
            _vk.EnumeratePhysicalDevices(_instance, &deviceCount, devices);
            _physicalDevice = devices[0]; // pick first

            _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out _memProps);

            // --- Logical Device ---
            uint familiesCount = 0;
            QueueFamilyProperties[] properties;
            _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, ref familiesCount, null);
            properties = new QueueFamilyProperties[familiesCount];
            _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, &familiesCount, properties);
            uint graphicsFamily = uint.MaxValue;
            uint presentFamily = uint.MaxValue;
            for (uint i = 0; i < properties.Length; i++)
            {
                if ((properties[(int)i].QueueFlags & QueueFlags.QueueGraphicsBit) != 0)
                {
                    graphicsFamily = i;
                    break;
                }

                Bool32 presentSupport;
                _khrSurface.GetPhysicalDeviceSurfaceSupport(_physicalDevice, i, _surface, out presentSupport);
                if (presentSupport)
                    presentFamily = i;
            }

            //if (graphicsFamily == uint.MaxValue || presentFamily == uint.MaxValue)
            //{
            //    throw new Exception("No suitable queue family found.");
            //}

            float priority = 1f;
            var queueInfo = new DeviceQueueCreateInfo(
                queueFamilyIndex: graphicsFamily,
                queueCount: 1,
                pQueuePriorities: &priority
            );


            string[] deviceExtensions = { "VK_KHR_swapchain" };
            var deviceExtPtrs = SilkMarshal.StringArrayToPtr(deviceExtensions);

            var deviceInfo = new DeviceCreateInfo(
                queueCreateInfoCount: 1,
                pQueueCreateInfos: &queueInfo,
                enabledExtensionCount: (uint)deviceExtensions.Length,
                ppEnabledExtensionNames: (byte**)deviceExtPtrs
            );

            if (_vk.CreateDevice(_physicalDevice, deviceInfo, null, out _device) != Result.Success)
                throw new Exception("Failed to create logical device.");

            ulong imageSize = (ulong)(_window.Size.X * _window.Size.Y * 4);

            ImageCreateInfo imgInfo = new()
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format.R8G8B8A8Unorm, // or same format as swapchain
                Extent = new Extent3D((uint?)_window.Size.X, (uint?)_window.Size.Y, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.ImageUsageColorAttachmentBit |
                        ImageUsageFlags.ImageUsageTransferSrcBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };
            if (_vk.CreateImage(_device, &imgInfo, null, out _offscreenImage) != Result.Success)
            {
                throw new Exception("Something failed");
            }

            _vk.GetImageMemoryRequirements(_device, _offscreenImage, out var reqs);
            uint memType = FindMemoryType(reqs.MemoryTypeBits,
                MemoryPropertyFlags.MemoryPropertyDeviceLocalBit, in _memProps);

            MemoryAllocateInfo alloc = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = reqs.Size,
                MemoryTypeIndex = memType
            };
            if (_vk.AllocateMemory(_device, &alloc, null, out _offscreenMemory) != Result.Success)
            {
                throw new Exception("Something failed");
            }
            if (_vk.BindImageMemory(_device, _offscreenImage, _offscreenMemory, 0) != Result.Success)
            {
                throw new Exception("Something failed");
            }

            // Create an ImageView
            ImageViewCreateInfo viewInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _offscreenImage,
                ViewType = ImageViewType.Type2D,
                Format = imgInfo.Format,
                SubresourceRange = new ImageSubresourceRange(
                    ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            if (_vk.CreateImageView(_device, &viewInfo, null, out _offscreenView) != Result.Success)
            {
                throw new Exception("Something failed");
            }

            BufferCreateInfo bufInfo = new()
            {
                SType = StructureType.BufferCreateInfo,
                Size = imageSize,
                Usage = BufferUsageFlags.BufferUsageTransferDstBit,
                SharingMode = SharingMode.Exclusive
            };
            _vk.CreateBuffer(_device, &bufInfo, null, out _buffer);

            // Allocate host-visible, coherent memory
            _vk.GetBufferMemoryRequirements(_device, _buffer, out reqs);
            memType = FindMemoryType(reqs.MemoryTypeBits,
                MemoryPropertyFlags.MemoryPropertyHostVisibleBit |
                MemoryPropertyFlags.MemoryPropertyHostCoherentBit, in _memProps);

            MemoryAllocateInfo allocInfo2 = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = reqs.Size,
                MemoryTypeIndex = memType
            };
            _vk.AllocateMemory(_device, &allocInfo2, null, out _deviceMemory);
            _vk.BindBufferMemory(_device, _buffer, _deviceMemory, 0);

            var fenceInfo = new FenceCreateInfo(flags: FenceCreateFlags.FenceCreateSignaledBit);
            var semInfo = new SemaphoreCreateInfo();

            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                _vk.CreateFence(_device, fenceInfo, null, out _inFlightFences[i]);
                _vk.CreateSemaphore(_device, semInfo, null, out _imageAvailableSemaphores[i]);
                _vk.CreateSemaphore(_device, semInfo, null, out _renderFinishedSemaphores[i]);
            }


            _vk.GetDeviceQueue(_device, graphicsFamily, 0, out _graphicsQueue);

            // --- Swapchain ---
            _swapchainExtension = new KhrSwapchain(_vk.Context);

            var surfaceFormat = new SurfaceFormatKHR(Format.B8G8R8A8Srgb, ColorSpaceKHR.ColorspaceSrgbNonlinearKhr);

            var swapchainInfo = new SwapchainCreateInfoKHR(
                surface: _surface,
                minImageCount: 2,
                imageFormat: surfaceFormat.Format,
                imageColorSpace: surfaceFormat.ColorSpace,
                imageExtent: new Extent2D((uint)_window.Size.X, (uint)_window.Size.Y),
                imageArrayLayers: 1,
                imageUsage: ImageUsageFlags.ImageUsageColorAttachmentBit,
                imageSharingMode: SharingMode.Exclusive,
                preTransform: SurfaceTransformFlagsKHR.SurfaceTransformIdentityBitKhr,
                compositeAlpha: CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                presentMode: PresentModeKHR.PresentModeFifoKhr,
                clipped: true
            );

            AllocationCallbacks callbacks;

            if (_swapchainExtension.CreateSwapchain(_device, ref swapchainInfo, null, out _swapchain) != Result.Success)
                throw new Exception("Failed to create swapchain.");

            // Get swapchain images
            uint swapchainImageCount = 0;
            _swapchainExtension.GetSwapchainImages(_device, _swapchain, ref swapchainImageCount, null);
            _swapchainImages = new Image[swapchainImageCount];
            _swapchainExtension.GetSwapchainImages(_device, _swapchain, &swapchainImageCount, _swapchainImages);

            // --- Image Views ---
            _swapchainImageViews = new ImageView[_swapchainImages.Length];
            for (int i = 0; i < _swapchainImages.Length; i++)
            {
                var createInfo = new ImageViewCreateInfo(
                    image: _swapchainImages[i],
                    viewType: ImageViewType.ImageViewType2D,
                    format: surfaceFormat.Format,
                    components: new ComponentMapping(),
                    subresourceRange: new ImageSubresourceRange(
                        aspectMask: ImageAspectFlags.ImageAspectColorBit,
                        baseMipLevel: 0,
                        levelCount: 1,
                        baseArrayLayer: 0,
                        layerCount: 1
                    )
                );
                if (_vk.CreateImageView(_device, createInfo, null, out _swapchainImageViews[i]) != Result.Success)
                    throw new Exception("Failed to create image view.");
            }

            // --- Render Pass ---
            var colorAttachment = new AttachmentDescription(
                format: surfaceFormat.Format,
                samples: SampleCountFlags.SampleCount1Bit,
                loadOp: AttachmentLoadOp.Clear,
                storeOp: AttachmentStoreOp.Store,
                stencilLoadOp: AttachmentLoadOp.DontCare,
                stencilStoreOp: AttachmentStoreOp.DontCare,
                initialLayout: ImageLayout.Undefined,
                finalLayout: ImageLayout.PresentSrcKhr
            );

            var colorAttachmentRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
            var subpass = new SubpassDescription(
                pipelineBindPoint: PipelineBindPoint.Graphics,
                colorAttachmentCount: 1,
                pColorAttachments: &colorAttachmentRef
            );
            var dep = new SubpassDependency(
                srcSubpass: Vk.SubpassExternal,
                dstSubpass: 0,
                srcStageMask: PipelineStageFlags.PipelineStageColorAttachmentOutputBit,
                dstStageMask: PipelineStageFlags.PipelineStageColorAttachmentOutputBit,
                srcAccessMask: 0,
                dstAccessMask: AccessFlags.AccessColorAttachmentWriteBit
            );

            var renderPassInfo = new RenderPassCreateInfo(
                attachmentCount: 1,
                pAttachments: &colorAttachment,
                subpassCount: 1,
                pSubpasses: &subpass,
                dependencyCount: 1,
                pDependencies: &dep
            );

            if (_vk.CreateRenderPass(_device, renderPassInfo, null, out _renderPass) != Result.Success)
                throw new Exception("Failed to create render pass.");


            fixed(ImageView* offscreenViewPtr = &_offscreenView)
            {
                FramebufferCreateInfo fbInfo = new()
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = _renderPass, // must match attachment format
                    AttachmentCount = 1,
                    PAttachments = offscreenViewPtr,
                    Width = _swapExtent.Width,
                    Height = _swapExtent.Height,
                    Layers = 1
                };

                Framebuffer _offscreenFramebuffer;
                if (_vk.CreateFramebuffer(_device, &fbInfo, null, out _offscreenFramebuffer) != Result.Success)
                {
                    throw new Exception("Something failed");
                }
            }

            // --- Framebuffers ---
            _framebuffers = new Framebuffer[_swapchainImageViews.Length];
            for (int i = 0; i < _swapchainImageViews.Length; i++)
            {
                fixed (ImageView* addr = &_swapchainImageViews[i])
                {
                    var framebufferInfo = new FramebufferCreateInfo(
                        renderPass: _renderPass,
                        attachmentCount: 1,
                        pAttachments: addr,
                        width: (uint)_window.Size.X,
                        height: (uint)_window.Size.Y,
                        layers: 1
                    );
                    if (_vk.CreateFramebuffer(_device, framebufferInfo, null, out _framebuffers[i]) != Result.Success)
                        throw new Exception("Failed to create framebuffer.");
                }
            }

            // --- Command Pool ---
            var poolInfo = new CommandPoolCreateInfo(
                queueFamilyIndex: graphicsFamily
            );
            _vk.CreateCommandPool(_device, poolInfo, null, out _commandPool);

            var vertBytes = File.ReadAllBytes(@"C:\Users\lithe\source\repos\ShowControl\ShowControl.Graphics\bin\Debug\net8.0\simple.vert.spv");
            var fragBytes = File.ReadAllBytes(@"C:\Users\lithe\source\repos\ShowControl\ShowControl.Graphics\bin\Debug\net8.0\kaleidoscope.frag.spv");

            _vertShaderModule = CreateShaderModule(vertBytes);
            _fragShaderModule = CreateShaderModule(fragBytes);

            var vertStageInfo = new PipelineShaderStageCreateInfo(
                stage: ShaderStageFlags.ShaderStageVertexBit,
                module: _vertShaderModule,
                pName: (byte*)SilkMarshal.StringToPtr("main")
            );

            var fragStageInfo = new PipelineShaderStageCreateInfo(
                stage: ShaderStageFlags.ShaderStageFragmentBit,
                module: _fragShaderModule,
                pName: (byte*)SilkMarshal.StringToPtr("main")
            );

            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = vertStageInfo;
            stages[1] = fragStageInfo;

            // --- No vertex buffers (use gl_VertexIndex) ---
            var vertexInput = new PipelineVertexInputStateCreateInfo(
                sType: StructureType.PipelineVertexInputStateCreateInfo,
                vertexBindingDescriptionCount: 0,
                vertexAttributeDescriptionCount: 0
            );

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo(
                sType: StructureType.PipelineInputAssemblyStateCreateInfo,
                topology: PrimitiveTopology.TriangleList,
                primitiveRestartEnable: false
            );

            // --- Viewport & scissor (match swapchain extent) ---
            _swapExtent = new Extent2D((uint)_window.Size.X, (uint)_window.Size.Y);
            var viewport = new Viewport(
                x: 0f, y: 0f,
                width: _swapExtent.Width,
                height: _swapExtent.Height,
                minDepth: 0f, maxDepth: 1f
            );
            var scissor = new Rect2D(new Offset2D(0, 0), _swapExtent);

            var viewportState = new PipelineViewportStateCreateInfo(
                sType: StructureType.PipelineViewportStateCreateInfo,
                viewportCount: 1, pViewports: &viewport,
                scissorCount: 1, pScissors: &scissor
            );

            // --- Rasterizer / multisample / blend ---
            var raster = new PipelineRasterizationStateCreateInfo(
                sType: StructureType.PipelineRasterizationStateCreateInfo,
                depthClampEnable: false,
                rasterizerDiscardEnable: false,
                polygonMode: PolygonMode.Fill,
                cullMode: CullModeFlags.CullModeNone, //CullModeFlags.CullModeBackBit,
                frontFace: FrontFace.CounterClockwise, // flip if needed
                depthBiasEnable: false,
                lineWidth: 1f
            );

            var multisample = new PipelineMultisampleStateCreateInfo(
                sType: StructureType.PipelineMultisampleStateCreateInfo,
                rasterizationSamples: SampleCountFlags.SampleCount1Bit
            );

            var colorBlendAttachment = new PipelineColorBlendAttachmentState(
                blendEnable: false,
                colorWriteMask:
                    ColorComponentFlags.ColorComponentRBit |
                    ColorComponentFlags.ColorComponentGBit |
                    ColorComponentFlags.ColorComponentBBit |
                    ColorComponentFlags.ColorComponentABit
            );

            var colorBlend = new PipelineColorBlendStateCreateInfo(
                sType: StructureType.PipelineColorBlendStateCreateInfo,
                logicOpEnable: false,
                attachmentCount: 1,
                pAttachments: &colorBlendAttachment
            );

            // When creating pipeline layout:
            var pushRange = new PushConstantRange(
                stageFlags: ShaderStageFlags.FragmentBit,
                offset: 0,
                size: (uint)Unsafe.SizeOf<PushData>()
            );

            // --- Empty layout (no descriptors/push constants) ---
            var layoutInfo = new PipelineLayoutCreateInfo(
                sType: StructureType.PipelineLayoutCreateInfo,
                pushConstantRangeCount: 1,
                pPushConstantRanges: &pushRange
            );
            if (_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _pipelineLayout) !=  Result.Success)
            {
                throw new Exception("Something failed");
            }

            // --- Graphics pipeline ---
            var pipeInfo = new GraphicsPipelineCreateInfo(
                sType: StructureType.GraphicsPipelineCreateInfo,
                stageCount: 2,
                pStages: stages,
                pVertexInputState: &vertexInput,
                pInputAssemblyState: &inputAssembly,
                pViewportState: &viewportState,
                pRasterizationState: &raster,
                pMultisampleState: &multisample,
                pDepthStencilState: null,
                pColorBlendState: &colorBlend,
                pDynamicState: null,
                layout: _pipelineLayout,
                renderPass: _renderPass, // must match your framebuffers' render pass
                subpass: 0
            );

            if (_vk.CreateGraphicsPipelines(_device, default, 1, in pipeInfo, null, out _pipeline) != Result.Success)
            {
                throw new Exception("Something failed");
            }

            // Free entrypoint strings AFTER pipeline creation
            //SilkMarshal.Free((nint)pMainVert);
            //SilkMarshal.Free((nint)pMainFrag);

            // --- Command Buffers ---
            var allocInfo = new CommandBufferAllocateInfo(
                commandPool: _commandPool,
                level: CommandBufferLevel.Primary,
                commandBufferCount: (uint)_framebuffers.Length
            );

            _commandBuffers = new CommandBuffer[_framebuffers.Length];
            fixed (CommandBuffer* pCmd = _commandBuffers)
            {
                _vk.AllocateCommandBuffers(_device, allocInfo, pCmd);
            }

            // Record simple clear command buffers
            for (int i = 0; i < _commandBuffers.Length; i++)
            {
                _vk.BeginCommandBuffer(_commandBuffers[i], new CommandBufferBeginInfo(sType: StructureType.CommandBufferBeginInfo));

                var clear = new ClearValue(new ClearColorValue(0.1f, 0.1f, 0.1f, 1f));
                var rp = new RenderPassBeginInfo(
                    sType: StructureType.RenderPassBeginInfo,
                    renderPass: _renderPass,
                    framebuffer: _framebuffers[i],
                    renderArea: new Rect2D(new Offset2D(0, 0),
                        new Extent2D((uint)_window.Size.X, (uint)_window.Size.Y)),
                    clearValueCount: 1, pClearValues: &clear);

                _vk.CmdBeginRenderPass(_commandBuffers[i], in rp, SubpassContents.Inline);
                _vk.CmdBindPipeline(_commandBuffers[i], PipelineBindPoint.Graphics, _pipeline);
                _vk.CmdDraw(_commandBuffers[i], 3, 1, 0, 0);   // <<< draw triangle
                _vk.CmdEndRenderPass(_commandBuffers[i]);
                _vk.EndCommandBuffer(_commandBuffers[i]);
            }
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
            // Wait for previous frame
            _vk.WaitForFences(_device, 1, in _inFlightFences[_currentFrame], true, ulong.MaxValue);
            _vk.ResetFences(_device, 1, in _inFlightFences[_currentFrame]);

            // Acquire an image and signal when it's available
            uint imageIndex = 0;
            _swapchainExtension.AcquireNextImage(
                _device, _swapchain, ulong.MaxValue,
                _imageAvailableSemaphores[_currentFrame], default, ref imageIndex);

            // Submit, waiting on the imageAvailable semaphore
            var waitStage = PipelineStageFlags.PipelineStageColorAttachmentOutputBit;
            fixed (Semaphore* pWaitSem = &_imageAvailableSemaphores[_currentFrame])
            fixed (Semaphore* pSignalSem = &_renderFinishedSemaphores[_currentFrame])
            fixed (CommandBuffer* pCmd = &_commandBuffers[imageIndex])
            {
                CommandBufferBeginInfo begin = new() { SType = StructureType.CommandBufferBeginInfo };
                _vk.BeginCommandBuffer(_commandBuffers[imageIndex], &begin);

                var clear = new ClearValue(new ClearColorValue(0.1f, 0.1f, 0.1f, 1f));
                RenderPassBeginInfo renderPassBeginInfo = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = _renderPass,                         // your VkRenderPass
                    Framebuffer = _framebuffers[imageIndex],          // your per-image framebuffer
                    RenderArea = new Rect2D
                    {
                        Offset = new Offset2D(0, 0),
                        Extent = _swapExtent                          // VkExtent2D for swapchain
                    },
                    ClearValueCount = 1,
                    PClearValues = &clear
                };

                _vk.CmdBeginRenderPass(_commandBuffers[imageIndex], &renderPassBeginInfo, SubpassContents.Inline);
                _vk.CmdBindPipeline(_commandBuffers[imageIndex], PipelineBindPoint.Graphics, _pipeline);

                PushData pc = new()
                {
                    Resolution = new System.Numerics.Vector2(_swapExtent.Width, _swapExtent.Height),
                    Time = (float)_stopwatch.Elapsed.TotalSeconds
                };

                _vk.CmdPushConstants(_commandBuffers[imageIndex], _pipelineLayout, ShaderStageFlags.FragmentBit,
                    0, (uint)Unsafe.SizeOf<PushData>(), &pc);

                _vk.CmdDraw(_commandBuffers[imageIndex], 3, 1, 0, 0);

                _vk.CmdEndRenderPass(_commandBuffers[imageIndex]);
                _vk.EndCommandBuffer(_commandBuffers[imageIndex]);

                var submitInfo = new SubmitInfo(
                    waitSemaphoreCount: 1, pWaitSemaphores: pWaitSem,
                    pWaitDstStageMask: &waitStage,
                    commandBufferCount: 1, pCommandBuffers: pCmd,
                    signalSemaphoreCount: 1, pSignalSemaphores: pSignalSem
                );

                _vk.QueueSubmit(_graphicsQueue, 1, ref submitInfo, _inFlightFences[_currentFrame]);

                // Present waits on renderFinished
                fixed (SwapchainKHR* pSwap = &_swapchain)
                {
                    var presentInfo = new PresentInfoKHR(
                        waitSemaphoreCount: 1, pWaitSemaphores: pSignalSem,
                        swapchainCount: 1, pSwapchains: pSwap,
                        pImageIndices: &imageIndex
                    );
                    _swapchainExtension.QueuePresent(_graphicsQueue, presentInfo);
                }
            }

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

/*
namespace ShowControl.Graphics
{
    public unsafe class HelloWorldGraphicsHost
    {
        private static IWindow window;
        private static Vk vk;
        private static Instance vkInstance;

        static SurfaceKHR surface;

        static PhysicalDevice physicalDevice;
        static Device device;

        static Queue graphicsQueue;
        static uint graphicsQueueIndex;

        static SwapchainKHR swapchain;
        static KhrSwapchain khrSwapchain;
        static Image[] swapchainImages;
        static ImageView[] swapchainImageViews;

        static CommandPool commandPool;
        static CommandBuffer[] commandBuffers;

        static Fence renderFence;

        static Format swapchainImageFormat;
        static Extent2D swapchainExtent;

        public static void Run()
        {
            new Thread(() =>
            {
                var options = WindowOptions.DefaultVulkan;

                // Create window
                window = Window.Create(options);

                // Set load event
                window.Load += OnLoad;

                // Run the window
                window.Run();
            })
            { IsBackground = true }.Start();
        }

        static void OnLoad()
        {
            // Initialize Vulkan
            vk = Vk.GetApi();

            khrSwapchain = new KhrSwapchain(vk.Context);

            // Get required extensions for the windowing system (platform-dependent)
            var extensions = window.VkSurface!.GetRequiredExtensions(out var extCount);
            Console.WriteLine("Required Extensions: " + extCount);

            // Prepare application info
            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)SilkMarshal.StringToPtr("SilkVulkanApp"),
                ApplicationVersion = new Version32(1, 0, 0),
                PEngineName = (byte*)SilkMarshal.StringToPtr("No Engine"),
                EngineVersion = new Version32(1, 0, 0),
                ApiVersion = Vk.Version12
            };

            // Create instance info
            var instanceInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = extCount,
                PpEnabledExtensionNames = extensions
            };

            // Create the Vulkan instance
            if (vk.CreateInstance(&instanceInfo, null, out vkInstance) != Result.Success)
            {
                throw new Exception("Failed to create Vulkan instance.");
            }

            // Create the window surface
            surface = window.VkSurface!.Create<AllocationCallbacks>(vkInstance.ToHandle(), null).ToSurface();

            Console.WriteLine("Vulkan instance and surface created successfully.");

            // Clean up unmanaged strings
            SilkMarshal.Free((nint)appInfo.PApplicationName);
            SilkMarshal.Free((nint)appInfo.PEngineName);

            // Pick physical device
            uint deviceCount = 0;
            vk.EnumeratePhysicalDevices(vkInstance, &deviceCount, (PhysicalDevice*)IntPtr.Zero);
            var devices = new PhysicalDevice[deviceCount];
            fixed (PhysicalDevice* devicesPtr = devices)
                vk.EnumeratePhysicalDevices(vkInstance, &deviceCount, devicesPtr);
            physicalDevice = devices[0];

            // Find queue family with graphics support and surface support
            uint queueFamilyCount = 0;
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, null);
            var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
            fixed (QueueFamilyProperties* queuePtr = queueFamilies)
                vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, queuePtr);

            var khrSurface = new KhrSurface(vk.Context);
            for (uint i = 0; i < queueFamilyCount; i++)
            {
                Bool32 supportsPresent;
                khrSurface.GetPhysicalDeviceSurfaceSupport(physicalDevice, i, surface, &supportsPresent);
                if ((queueFamilies[i].QueueFlags & QueueFlags.QueueGraphicsBit) != 0 && supportsPresent.Value != 0)
                {
                    graphicsQueueIndex = i;
                    break;
                }
            }

            // Create logical device
            float priority = 1.0f;
            var queueCreateInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = graphicsQueueIndex,
                QueueCount = 1,
                PQueuePriorities = &priority
            };

            var deviceCreateInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueCreateInfo
            };

            if (vk.CreateDevice(physicalDevice, &deviceCreateInfo, null, out device) != Result.Success)
                throw new Exception("Failed to create logical device.");

            vk.GetDeviceQueue(device, graphicsQueueIndex, 0, out graphicsQueue);

            // Create swapchain
            SurfaceFormatKHR surfaceFormat = ChooseSurfaceFormat();
            swapchainImageFormat = surfaceFormat.Format;

            SurfaceCapabilitiesKHR surfaceCaps;
            khrSurface.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, surface, &surfaceCaps);

            swapchainExtent = surfaceCaps.CurrentExtent;

            // Query surface capabilities first
            SurfaceCapabilitiesKHR surfaceCapabilities;
            khrSurface.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, surface, out surfaceCapabilities);

            // Clamp swapchain extent to allowed bounds
            swapchainExtent = surfaceCapabilities.CurrentExtent;
            if (swapchainExtent.Width == uint.MaxValue) // Sometimes special case meaning no fixed size
            {
                swapchainExtent.Width = (uint)Math.Clamp(window.Size.X, surfaceCapabilities.MinImageExtent.Width, surfaceCapabilities.MaxImageExtent.Width);
                swapchainExtent.Height = (uint)Math.Clamp(window.Size.Y, surfaceCapabilities.MinImageExtent.Height, surfaceCapabilities.MaxImageExtent.Height);
            }

            // Choose image count (min + 1, but max respected)
            uint imageCount = surfaceCapabilities.MinImageCount + 1;
            if (surfaceCapabilities.MaxImageCount > 0 && imageCount > surfaceCapabilities.MaxImageCount)
            {
                imageCount = surfaceCapabilities.MaxImageCount;
            }

            var swapchainCreateInfo = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = surface,
                MinImageCount = imageCount,
                ImageFormat = swapchainImageFormat,
                ImageColorSpace = surfaceFormat.ColorSpace,
                ImageExtent = swapchainExtent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ImageUsageColorAttachmentBit,
                ImageSharingMode = SharingMode.Exclusive,
                QueueFamilyIndexCount = 0,
                PQueueFamilyIndices = null,
                PreTransform = surfaceCapabilities.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr,
                PresentMode = PresentModeKHR.PresentModeFifoKhr, // Guaranteed to be supported
                Clipped = true,
                OldSwapchain = default
            };

            if (khrSwapchain.CreateSwapchain(device, &swapchainCreateInfo, null, out swapchain) != Result.Success)
                throw new Exception("Failed to create swapchain.");

            // Get swapchain images
            imageCount = 0;
            khrSwapchain.GetSwapchainImages(device, swapchain, &imageCount, null);
            swapchainImages = new Image[imageCount];
            fixed (Image* imagesPtr = swapchainImages)
                khrSwapchain.GetSwapchainImages(device, swapchain, &imageCount, imagesPtr);

            // Create image views
            swapchainImageViews = new ImageView[imageCount];
            for (int i = 0; i < imageCount; i++)
            {
                var viewInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = swapchainImages[i],
                    ViewType = ImageViewType.ImageViewType2D,
                    Format = swapchainImageFormat,
                    Components = new ComponentMapping(),
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = ImageAspectFlags.ImageAspectColorBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    }
                };

                if (vk.CreateImageView(device, &viewInfo, null, out swapchainImageViews[i]) != Result.Success)
                    throw new Exception("Failed to create image view.");
            }

            // Create command pool
            var cmdPoolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = graphicsQueueIndex,
                Flags = CommandPoolCreateFlags.CommandPoolCreateResetCommandBufferBit
            };
            vk.CreateCommandPool(device, &cmdPoolInfo, null, out commandPool);

            // Allocate command buffers
            commandBuffers = new CommandBuffer[imageCount];
            var allocInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = (uint)imageCount
            };
            fixed (CommandBuffer* cmdBufPtr = commandBuffers)
                vk.AllocateCommandBuffers(device, &allocInfo, cmdBufPtr);

            // Record command buffers
            for (int i = 0; i < commandBuffers.Length; i++)
            {
                var beginInfo = new CommandBufferBeginInfo
                {
                    SType = StructureType.CommandBufferBeginInfo
                };

                vk.BeginCommandBuffer(commandBuffers[i], &beginInfo);

                var clearColor = new ClearValue
                {
                    Color = new ClearColorValue(0f, 0.1f, 0.4f, 1f) // Blue-ish clear
                };

                var renderPassInfo = new RenderPassBeginInfo(); // Dummy; just clear with pipeline barriers (skipped for minimal)

                var barrier = new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    SrcAccessMask = 0,
                    DstAccessMask = AccessFlags.AccessColorAttachmentWriteBit,
                    OldLayout = ImageLayout.Undefined,
                    NewLayout = ImageLayout.General,
                    SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    Image = swapchainImages[i],
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = ImageAspectFlags.ImageAspectColorBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    }
                };

                vk.CmdPipelineBarrier(commandBuffers[i],
                    PipelineStageFlags.PipelineStageTopOfPipeBit,
                    PipelineStageFlags.PipelineStageColorAttachmentOutputBit,
                    0,
                    0, null,
                    0, null,
                    1, &barrier);

                vk.CmdClearColorImage(commandBuffers[i],
                    swapchainImages[i],
                    ImageLayout.General,
                    &clearColor.Color,
                    1,
                    &barrier.SubresourceRange);

                vk.EndCommandBuffer(commandBuffers[i]);
            }

            // Create fence
            var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
            vk.CreateFence(device, &fenceInfo, null, out renderFence);
        }

        static void OnRender(double delta)
        {
            uint imageIndex = 0;
            fixed (Fence* fencePtr = &renderFence)
            {
                khrSwapchain.AcquireNextImage(device, swapchain, ulong.MaxValue, default, renderFence, &imageIndex);
                vk.WaitForFences(device, 1, fencePtr, true, ulong.MaxValue);
                vk.ResetFences(device, 1, fencePtr);
            }

            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1
            };

            fixed (CommandBuffer* cb = &commandBuffers[imageIndex])
                submitInfo.PCommandBuffers = cb;

            vk.QueueSubmit(graphicsQueue, 1, &submitInfo, renderFence);
            vk.QueueWaitIdle(graphicsQueue);

            fixed (SwapchainKHR* swapchainPtr = &swapchain)
            {
                var presentInfo = new PresentInfoKHR
                {
                    SType = StructureType.PresentInfoKhr,
                    SwapchainCount = 1,
                    PSwapchains = swapchainPtr,
                    PImageIndices = &imageIndex
                };

                khrSwapchain.QueuePresent(graphicsQueue, &presentInfo);
            }
        }

        static void OnClose()
        {
            vk.DeviceWaitIdle(device);

            foreach (var view in swapchainImageViews)
                vk.DestroyImageView(device, view, null);

            vk.DestroyCommandPool(device, commandPool, null);
            vk.DestroyDevice(device, null);
            khrSwapchain.DestroySwapchain(device, swapchain, null);
            var khrSurface = new KhrSurface(vk.Context);
            khrSurface.DestroySurface(vkInstance, surface, null);
            vk.DestroyInstance(vkInstance, null);

            Console.WriteLine("Vulkan cleanup complete.");
        }

        static SurfaceFormatKHR ChooseSurfaceFormat()
        {
            uint formatCount = 0;
            var khrSurface = new KhrSurface(vk.Context);
            khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, &formatCount, null);
            var formats = new SurfaceFormatKHR[formatCount];
            fixed (SurfaceFormatKHR* formatsPtr = formats)
                khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, &formatCount, formatsPtr);

            return formats[0];
        }
    }
}
*/