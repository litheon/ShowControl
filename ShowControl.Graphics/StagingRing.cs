using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Buffer = Silk.NET.Vulkan.Buffer;
using Device = Silk.NET.Vulkan.Device;

namespace ShowControl.Graphics
{
    // ---------- Triple-buffered staging ring ----------
    unsafe sealed class StagingRing : IDisposable
    {
        private const int RingSize = 3;

        private readonly Vk _vk;
        private readonly Device _device;
        private readonly PhysicalDeviceMemoryProperties _memProps;
        private readonly ulong _sizeBytes;

        private Buffer[] _buf = new Buffer[RingSize];
        private DeviceMemory[] _mem = new DeviceMemory[RingSize];
        private void*[] _mapped = new void*[RingSize];
        private VulkanMappedMemoryManager[] _mmm = new VulkanMappedMemoryManager[RingSize];
        private Memory<byte>[] _memory = new Memory<byte>[RingSize];
        private Fence[] _fence = new Fence[RingSize];

        private uint _transferQFamilyIndex;
        private bool _hostCoherent;

        public int WriteIndex { get; private set; } = 0;
        public int ReadIndex { get; private set; } = RingSize - 1;

        public Memory<byte> CurrentReadMemory => _memory[ReadIndex];
        public void* CurrentReadPtr => _mapped[ReadIndex];
        public ulong SizeBytes => _sizeBytes;
        public Fence CurrentWriteFence => _fence[WriteIndex];
        public Buffer CurrentWriteBuffer => _buf[WriteIndex];

        public StagingRing(
            Vk vk,
            Device device,
            PhysicalDeviceMemoryProperties memProps,
            uint transferQueueFamilyIndex,
            ulong sizeBytes)
        {
            _vk = vk;
            _device = device;
            _memProps = memProps;
            _transferQFamilyIndex = transferQueueFamilyIndex;
            _sizeBytes = sizeBytes;

            Create();
        }

        public void Dispose()
        {
            Destroy();
            GC.SuppressFinalize(this);
        }

        private void Create()
        {
            // Pick HOST_VISIBLE, prefer HOST_CACHED if available for faster reads; keep COHERENT if you can get it.
            // We'll detect if COHERENT is actually present and fall back to Invalidate if not.
            for (int i = 0; i < RingSize; i++)
            {
                // Buffer
                BufferCreateInfo bufInfo = new()
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = _sizeBytes,
                    Usage = BufferUsageFlags.BufferUsageTransferDstBit,
                    SharingMode = SharingMode.Exclusive,
                    QueueFamilyIndexCount = 1,
                };
                fixed (uint* q = &_transferQFamilyIndex)
                {
                    bufInfo.PQueueFamilyIndices = q;
                    _vk.CreateBuffer(_device, &bufInfo, null, out _buf[i]).ThrowOnError();
                }

                // Requirements
                _vk.GetBufferMemoryRequirements(_device, _buf[i], out var reqs);

                // Choose a memory type
                var flagsWanted =
                    MemoryPropertyFlags.MemoryPropertyHostVisibleBit |
                    MemoryPropertyFlags.MemoryPropertyHostCoherentBit |
                    MemoryPropertyFlags.MemoryPropertyHostCachedBit;

                uint typeIdx = FindMemoryType(reqs.MemoryTypeBits, flagsWanted, _memProps,
                                              out MemoryPropertyFlags actual);

                _hostCoherent = (actual & MemoryPropertyFlags.MemoryPropertyHostCoherentBit) != 0;

                // Allocate
                MemoryAllocateInfo alloc = new()
                {
                    SType = StructureType.MemoryAllocateInfo,
                    AllocationSize = reqs.Size,
                    MemoryTypeIndex = typeIdx
                };
                _vk.AllocateMemory(_device, &alloc, null, out _mem[i]).ThrowOnError();

                _vk.BindBufferMemory(_device, _buf[i], _mem[i], 0).ThrowOnError();

                // Persistent map
                void* mapped = null;
                _vk.MapMemory(_device, _mem[i], 0, _sizeBytes, 0, &mapped).ThrowOnError();
                _mapped[i] = mapped;

                // Wrap in Memory<byte>
                _mmm[i] = new VulkanMappedMemoryManager(mapped, (int)_sizeBytes);
                _memory[i] = _mmm[i].Memory;

                // Fence (start signaled so first Wait returns immediately)
                FenceCreateInfo fi = new(flags: FenceCreateFlags.FenceCreateSignaledBit);
                _vk.CreateFence(_device, fi, null, out _fence[i]).ThrowOnError();
            }
        }

        private void Destroy()
        {
            for (int i = 0; i < RingSize; i++)
            {
                if (_mapped[i] != null)
                {
                    _vk.UnmapMemory(_device, _mem[i]);
                    _mapped[i] = null;
                }

                _mmm[i]?.Dispose();
                _mmm[i] = null;

                if (_fence[i].Handle != 0)
                {
                    _vk.DestroyFence(_device, _fence[i], null);
                    _fence[i] = default;
                }

                if (_buf[i].Handle != 0)
                {
                    _vk.DestroyBuffer(_device, _buf[i], null);
                    _buf[i] = default;
                }

                if (_mem[i].Handle != 0)
                {
                    _vk.FreeMemory(_device, _mem[i], null);
                    _mem[i] = default;
                }
            }
        }

        // Advance indices after you've queued the GPU write for this slot.
        public void Advance()
        {
            ReadIndex = WriteIndex;                // read the one we just finished (next frame)
            WriteIndex = (WriteIndex + 1) % RingSize;
        }

        // Wait → reset for the write slot you’re about to reuse
        public void WaitAndResetWriteFence()
        {
            _vk.WaitForFences(_device, 1, in _fence[WriteIndex], true, ulong.MaxValue).ThrowOnError();
            _vk.ResetFences(_device, 1, in _fence[WriteIndex]).ThrowOnError();
        }

        // Ensure host visibility if memory is non-coherent
        public void InvalidateReadIfNeeded()
        {
            if (_hostCoherent) return;

            MappedMemoryRange rng = new()
            {
                SType = StructureType.MappedMemoryRange,
                Memory = _mem[ReadIndex],
                Offset = 0,
                Size = _sizeBytes
            };
            _vk.InvalidateMappedMemoryRanges(_device, 1, &rng).ThrowOnError();
        }

        private static uint FindMemoryType(
            uint typeBits,
            MemoryPropertyFlags wanted,
            in PhysicalDeviceMemoryProperties memProps,
            out MemoryPropertyFlags actualChosenFlags)
        {
            // Try with HOST_CACHED+COHERENT+VISIBLE, then relax to COHERENT+VISIBLE, then just VISIBLE.
            MemoryPropertyFlags[] prefs = new[]
            {
                wanted,
                MemoryPropertyFlags.MemoryPropertyHostVisibleBit | MemoryPropertyFlags.MemoryPropertyHostCoherentBit,
                MemoryPropertyFlags.MemoryPropertyHostVisibleBit
            };

            foreach (var candidate in prefs)
            {
                for (uint i = 0; i < memProps.MemoryTypeCount; i++)
                {
                    bool supported = (typeBits & (1u << (int)i)) != 0;
                    bool hasProps = (memProps.MemoryTypes[(int)i].PropertyFlags & candidate) == candidate;
                    if (supported && hasProps)
                    {
                        actualChosenFlags = memProps.MemoryTypes[(int)i].PropertyFlags;
                        return i;
                    }
                }
            }

            throw new InvalidOperationException("No suitable HOST_VISIBLE memory type found.");
        }
    }
}
