using ShowControl.Animation;
using ShowControl.Devices.DMX;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Rendering
{
    public interface IRenderSurface
    {
        public void Render(
            Span<Pixel> sourcePixels,
            Span<byte> renderTarget,
            RenderSettings renderSettings);

        public int TotalPixelCount { get; }

        public int TotalChannelCount { get; }

        public IDmxDevice[] GetDevices();
    }
}
