using ShowControl.Animation;
using ShowControl.Devices.Audio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ShowControl.Rendering
{
    public class RenderSurface : IRenderSurface
    {
        private IRenderSurface[] _renderSurfaces;
        private RenderSettings _renderSettings;
        public RenderSurface(
            RenderSettings renderSettings = null,
            params IRenderSurface[] renderSurfaces
            )
        {
            _renderSettings = renderSettings;
            _renderSurfaces = renderSurfaces;
            TotalPixelCount = _renderSurfaces.Sum(r => r.TotalPixelCount);
            TotalChannelCount = _renderSurfaces.Sum(r => r.TotalChannelCount);
        }

        public void Render(
            Span<Pixel> sourcePixels,
            Span<byte> renderTarget,
            RenderSettings renderSettings)
        {
            if (sourcePixels.Length != TotalPixelCount)
            {
                throw new ArgumentException("Pixel count mismatch");
            }
            int pixelOffset = 0;
            int channelOffset = 0;
            foreach (var surface in _renderSurfaces)
            {
                var renderPixels = sourcePixels.Slice(pixelOffset, surface.TotalPixelCount);
                var targetChannels = renderTarget.Slice(channelOffset, surface.TotalChannelCount);
                surface.Render(renderPixels, targetChannels, _renderSettings ?? renderSettings);
                pixelOffset += surface.TotalPixelCount;
                channelOffset += surface.TotalChannelCount;
            }
        }

        public int TotalPixelCount { get; private set; }

        public int TotalChannelCount { get; private set; }
    }
}
