using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Graphics
{
    // ---------- MemoryManager wrapper over mapped Vulkan memory ----------
    // Wraps a raw pointer + length as Memory<byte> without copying.
    unsafe sealed class VulkanMappedMemoryManager : MemoryManager<byte>
    {
        private readonly byte* _ptr;
        private readonly int _length;
        private bool _disposed;

        public VulkanMappedMemoryManager(void* ptr, int length)
        {
            _ptr = (byte*)ptr;
            _length = length;
        }

        public override Span<byte> GetSpan()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VulkanMappedMemoryManager));
            return new Span<byte>(_ptr, _length);
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VulkanMappedMemoryManager));
            if ((uint)elementIndex > (uint)_length) throw new ArgumentOutOfRangeException(nameof(elementIndex));
            return new MemoryHandle(_ptr + elementIndex);
        }

        public override void Unpin() { }

        protected override void Dispose(bool disposing) => _disposed = true;

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
