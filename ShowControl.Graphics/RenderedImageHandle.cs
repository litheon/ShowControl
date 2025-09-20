using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Graphics
{
    public sealed class RenderedImageHandle<T> : IDisposable
    {
        public static ConcurrentDictionary<RenderedImageHandle<T>, RenderedImageHandle<T>> Handles = new ConcurrentDictionary<RenderedImageHandle<T>, RenderedImageHandle<T>>();
        public static RenderedImageHandle<T> Empty = new RenderedImageHandle<T>(Memory<T>.Empty, (u, v) => { }, 0, 0);
        private Action<int, ulong> _releaseAction;
        public Memory<T> Memory { get; init; }
        public ulong ProducedValue { get; init; }
        public int Slot { get; init; }
        int _cookie;
        public RenderedImageHandle(Memory<T> memory, Action<int, ulong> release, int slot, ulong producedValue)
        {
            _releaseAction = release;
            Memory = memory;
            Slot = slot;
            ProducedValue = producedValue;
            if (!Handles.TryAdd(this, this))
            {
                int x = 0;
            }
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _cookie, 1, 0) == 0)
            {
                _releaseAction(Slot, ProducedValue);
                if (!Handles.TryRemove(this, out var item))
                {
                    int x = 0;
                }
            }
        }
    }
}
