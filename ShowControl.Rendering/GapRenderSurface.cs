using System;
using ShowControl.Animation;
using ShowControl.Devices.DMX;

namespace ShowControl.Rendering
{
    /// <summary>
    /// Unaddressed channels between two fixtures on the same controller. Consumes channel space but
    /// no pixels, which keeps the sequential surface walk aligned when a venue leaves a hole in a
    /// controller's channel map.
    /// <para>
    /// Renders nothing: the target buffer is zero-initialised and no other surface writes here, so
    /// the gap stays dark.
    /// </para>
    /// </summary>
    public sealed class GapRenderSurface : IRenderSurface
    {
        private readonly int _channelCount;

        public GapRenderSurface(int channelCount)
        {
            if (channelCount <= 0) throw new ArgumentOutOfRangeException(nameof(channelCount));
            _channelCount = channelCount;
        }

        public int TotalPixelCount => 0;

        public int TotalChannelCount => _channelCount;

        public IDmxDevice[] GetDevices() => Array.Empty<IDmxDevice>();

        public void Render(Span<Pixel> sourcePixels, Span<byte> targetChannels, RenderSettings renderSettings)
        {
        }
    }
}
