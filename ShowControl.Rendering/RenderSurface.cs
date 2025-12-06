using ShowControl.Animation;
using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
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

        public static IRenderSurface Create(
            RenderSettings renderSettings = null,
            params IRenderSurface[] renderSurfaces
            )
        {
            return new RenderSurface(renderSettings, renderSurfaces);
        }

        public static IRenderSurface Create(
            RenderSettings renderSettings = null,
            params IDmxDevice[] renderSurfaces
            )
        {
            return new RenderSurface(renderSettings, renderSurfaces.Select(r => new DmxDeviceRenderSurface(r)).ToArray());
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
                surface.Render(renderPixels, targetChannels, _renderSettings.Combine(renderSettings));
                pixelOffset += surface.TotalPixelCount;
                channelOffset += surface.TotalChannelCount;
            }
        }

        public IDmxDevice[] GetDevices()
        {
            return _renderSurfaces.SelectMany(r => r.GetDevices()).ToArray();
        }

        public int TotalPixelCount { get; private set; }

        public int TotalChannelCount { get; private set; }
    }
}
