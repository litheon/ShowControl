using System;
using ShowControl.Animation;
using ShowControl.Devices.DMX;
using ShowControl.PixelMapping;

namespace ShowControl.Rendering
{
    /// <summary>
    /// Renders a contiguous run of pixels through one DMX fixture definition.
    /// <para>
    /// Replaces the old arrangement of one <see cref="DmxDeviceRenderSurface"/> per pixel — 11,520
    /// wrappers around a single shared <c>WS2812B.Instance</c>, each allocating a three-byte array
    /// every frame. One surface per fixture instead, with its own private device instance so the
    /// shared-mutable-singleton hazard goes away, and the per-frame allocation with it.
    /// </para>
    /// </summary>
    public sealed class PixelRunRenderSurface : IRenderSurface
    {
        private readonly IDmxDevice _device;
        private readonly int _pixelCount;
        private readonly ColorOrder _colorOrder;
        private readonly int _channelsPerPixel;

        public PixelRunRenderSurface(IDmxDevice device, int pixelCount, ColorOrder colorOrder = ColorOrder.Rgb)
        {
            if (pixelCount <= 0) throw new ArgumentOutOfRangeException(nameof(pixelCount));

            _device = device ?? throw new ArgumentNullException(nameof(device));
            _pixelCount = pixelCount;
            _colorOrder = colorOrder;
            _channelsPerPixel = device.ChannelCount;
        }

        public int TotalPixelCount => _pixelCount;

        public int TotalChannelCount => _pixelCount * _channelsPerPixel;

        public IDmxDevice[] GetDevices() => new IDmxDevice[] { _device };

        public void Render(Span<Pixel> sourcePixels, Span<byte> targetChannels, RenderSettings renderSettings)
        {
            byte masterFader = 0;
            byte ultraViolet = 0;
            if (renderSettings != null)
            {
                masterFader = (byte)renderSettings.MasterFader;
                ultraViolet = (byte)renderSettings.UV;
            }

            for (int i = 0; i < _pixelCount; i++)
            {
                _device.Color = sourcePixels[i].Color;
                _device.Mode = DmxDeviceMode.RGB;
                if (renderSettings != null)
                {
                    _device.MasterFader = masterFader;
                    _device.MaximumUltraViolet = ultraViolet;
                }

                // GetDMXVector applies the master fader and writes the faded colour back to Color,
                // so it must be called exactly once per pixel, after Color is set.
                var dmxVector = _device.GetDMXVector();
                var destination = targetChannels.Slice(i * _channelsPerPixel, _channelsPerPixel);
                for (int channel = 0; channel < dmxVector.Length; channel++)
                {
                    destination[channel] = dmxVector[channel];
                }
                _colorOrder.Apply(destination);
            }
        }
    }
}
