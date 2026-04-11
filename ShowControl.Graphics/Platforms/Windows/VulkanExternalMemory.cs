#if WINDOWS
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Runtime.InteropServices;
using VkDevice = Silk.NET.Vulkan.Device;
using VkImage = Silk.NET.Vulkan.Image;

namespace ShowControl.Graphics.Platforms.Windows;

/// <summary>
/// Manages Vulkan resources that import shared textures from DirectX 11.
/// Uses VK_KHR_external_memory_win32 to import D3D11 NT handles.
/// </summary>
public unsafe class VulkanExternalMemory : IDisposable
{
    private readonly Vk _vk;
    private readonly VkDevice _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly KhrExternalMemoryWin32 _externalMemoryWin32;

    private VkImage _sharedImage;
    private DeviceMemory _sharedMemory;
    private nint _importedHandle;
    private uint _width;
    private uint _height;
    private bool _disposed;

    public VkImage SharedImage => _sharedImage;
    public uint Width => _width;
    public uint Height => _height;

    public VulkanExternalMemory(Vk vk, Instance instance, VkDevice device, PhysicalDevice physicalDevice)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;

        if (!vk.TryGetDeviceExtension(instance, device, out _externalMemoryWin32!))
        {
            throw new InvalidOperationException("VK_KHR_external_memory_win32 extension not available");
        }
    }

    /// <summary>
    /// Imports a shared texture from D3D11 using the provided NT handle.
    /// </summary>
    public void ImportSharedTexture(nint d3d11Handle, uint width, uint height, uint graphicsQueueFamilyIndex)
    {
        if (_sharedImage.Handle != 0)
        {
            DestroySharedImage();
        }

        _width = width;
        _height = height;
        _importedHandle = d3d11Handle;

        // Use D3D11TextureBit (NT handles) - the modern, well-supported path
        var externalMemoryInfo = new ExternalMemoryImageCreateInfo
        {
            SType = StructureType.ExternalMemoryImageCreateInfo,
            HandleTypes = ExternalMemoryHandleTypeFlags.D3D11TextureBit
        };

        System.Diagnostics.Debug.WriteLine($"[VulkanExternalMemory] Creating image for D3D11 NT handle import: {width}x{height}");

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            PNext = &externalMemoryInfo,
            ImageType = ImageType.Type2D,
            Format = Format.B8G8R8A8Srgb, // Compatible with DXGI_FORMAT_B8G8R8A8_UNORM
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            QueueFamilyIndexCount = 1,
            PQueueFamilyIndices = &graphicsQueueFamilyIndex,
            InitialLayout = ImageLayout.Undefined
        };

        try
        {
            fixed (VkImage* pImage = &_sharedImage)
            {
                var result = _vk.CreateImage(_device, &imageInfo, null, pImage);
                System.Diagnostics.Debug.WriteLine($"[VulkanExternalMemory] CreateImage result: {result}");
                result.ThrowOnError();
            }

            // Get memory requirements
            _vk.GetImageMemoryRequirements(_device, _sharedImage, out var memReqs);
            System.Diagnostics.Debug.WriteLine($"[VulkanExternalMemory] Memory requirements: size={memReqs.Size}, alignment={memReqs.Alignment}, typeBits=0x{memReqs.MemoryTypeBits:X}");

            // Query compatible memory types for the D3D11 NT handle
            var handleProps = new MemoryWin32HandlePropertiesKHR
            {
                SType = StructureType.MemoryWin32HandlePropertiesKhr
            };
            _externalMemoryWin32.GetMemoryWin32HandleProperties(
                _device,
                ExternalMemoryHandleTypeFlags.D3D11TextureBit,
                d3d11Handle,
                &handleProps);
            System.Diagnostics.Debug.WriteLine($"[VulkanExternalMemory] NT handle compatible memoryTypeBits: 0x{handleProps.MemoryTypeBits:X}");

            // Intersect image memory requirements with handle-compatible types
            var compatibleTypeBits = memReqs.MemoryTypeBits & handleProps.MemoryTypeBits;
            if (compatibleTypeBits == 0)
                throw new InvalidOperationException($"No compatible memory type: image requires 0x{memReqs.MemoryTypeBits:X}, handle supports 0x{handleProps.MemoryTypeBits:X}");

            var memTypeIndex = FindMemoryType(compatibleTypeBits, MemoryPropertyFlags.DeviceLocalBit);
            System.Diagnostics.Debug.WriteLine($"[VulkanExternalMemory] Using memory type index: {memTypeIndex}");

            System.Diagnostics.Debug.WriteLine($"[VulkanExternalMemory] Importing D3D11 NT handle: 0x{d3d11Handle:X}");

            // Import memory from D3D11 NT handle
            var importMemoryInfo = new ImportMemoryWin32HandleInfoKHR
            {
                SType = StructureType.ImportMemoryWin32HandleInfoKhr,
                HandleType = ExternalMemoryHandleTypeFlags.D3D11TextureBit,
                Handle = d3d11Handle,
                Name = 0
            };

            // For D3D11 textures, we need dedicated allocation
            var dedicatedAllocInfo = new MemoryDedicatedAllocateInfo
            {
                SType = StructureType.MemoryDedicatedAllocateInfo,
                PNext = &importMemoryInfo,
                Image = _sharedImage,
                Buffer = default
            };

            var memAllocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                PNext = &dedicatedAllocInfo,
                AllocationSize = memReqs.Size,
                MemoryTypeIndex = memTypeIndex
            };

            fixed (DeviceMemory* pMemory = &_sharedMemory)
            {
                var result = _vk.AllocateMemory(_device, &memAllocInfo, null, pMemory);
                System.Diagnostics.Debug.WriteLine($"[VulkanExternalMemory] AllocateMemory result: {result}");
                result.ThrowOnError();
            }

            var bindResult = _vk.BindImageMemory(_device, _sharedImage, _sharedMemory, 0);
            System.Diagnostics.Debug.WriteLine($"[VulkanExternalMemory] BindImageMemory result: {bindResult}");
            bindResult.ThrowOnError();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VulkanExternalMemory] FAILED to import D3D11 texture: {ex.Message}");
            throw;
        }

        System.Diagnostics.Debug.WriteLine($"[VulkanExternalMemory] Imported D3D11 shared texture {width}x{height}, handle=0x{d3d11Handle:X}");
    }

    /// <summary>
    /// Transitions the shared image to the specified layout.
    /// </summary>
    public void TransitionImageLayout(CommandBuffer cmd, ImageLayout oldLayout, ImageLayout newLayout)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = _sharedImage,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        PipelineStageFlags srcStage;
        PipelineStageFlags dstStage;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.General)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.MemoryReadBit;
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.AllCommandsBit;
        }
        else if (oldLayout == ImageLayout.General && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.MemoryReadBit;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            srcStage = PipelineStageFlags.AllCommandsBit;
            dstStage = PipelineStageFlags.TransferBit;
        }
        else
        {
            barrier.SrcAccessMask = AccessFlags.MemoryWriteBit;
            barrier.DstAccessMask = AccessFlags.MemoryReadBit;
            srcStage = PipelineStageFlags.AllCommandsBit;
            dstStage = PipelineStageFlags.AllCommandsBit;
        }

        _vk.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var memProps);

        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Failed to find suitable memory type for external import");
    }

    private void DestroySharedImage()
    {
        if (_sharedImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _sharedImage, null);
            _sharedImage = default;
        }

        if (_sharedMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _sharedMemory, null);
            _sharedMemory = default;
        }

        _importedHandle = 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            DestroySharedImage();
            _disposed = true;
        }
    }
}
#endif
