using ShowControl.Animation;
using ShowControl.Devices.DMX;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace ShowControl.Rendering
{
    public class DmxDeviceRenderSurface : IRenderSurface
    {
        private IDmxDevice _dmxDevice;
        public DmxDeviceRenderSurface(
            IDmxDevice dmxDevice)
        {
            _dmxDevice = dmxDevice;
        }

        public void Render(
            Span<Pixel> sourcePixels,
            Span<byte> targetChannels,
            RenderSettings renderSettings)
        {
            var pixel = sourcePixels[0];

            _dmxDevice.Color = pixel.Color;
            _dmxDevice.Mode = pixel.UV > 0 ? DmxDeviceMode.UV : DmxDeviceMode.RGB;
            if (_dmxDevice.Mode == DmxDeviceMode.SoundReactive)
            {
                _dmxDevice.SoundReactivitySensitivity = pixel.SoundReactivity;
            }
            if (renderSettings != null)
            {
                _dmxDevice.MasterFader = (byte)renderSettings.MasterFader;
                _dmxDevice.UltraViolet = (byte)renderSettings.UV;
            }

            var dmxVector = _dmxDevice.GetDMXVector();
            for (int i = 0; i < dmxVector.Length; i++)
            {
                targetChannels[i] = dmxVector[i];
            }
        }

        public IDmxDevice[] GetDevices()
        {
            return new IDmxDevice[] { _dmxDevice };
        }

        public int TotalPixelCount => 1;

        public int TotalChannelCount => _dmxDevice.ChannelCount;
    }
}
