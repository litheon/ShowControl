using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using ShowControl.PixelMapping;

namespace ShowControl.Devices.Output
{
    /// <summary>
    /// Builds the output devices described by the live pixel map.
    /// <para>
    /// The device list used to be a hardcoded array here, with unused venues commented out. Because
    /// the channel split was positional, commenting a line in or out silently re-addressed every
    /// device after it. Fixtures now carry their own endpoint and channel offset, so a venue toggle
    /// is the <c>Enabled</c> flag on a fixture and cannot disturb anything else.
    /// </para>
    /// <para>
    /// Devices are returned in <see cref="PixelMapLayout.Outputs"/> order, which is the contract
    /// <c>RenderService</c> relies on to pair each device with its channel range.
    /// </para>
    /// </summary>
    public class DmxDeviceEnumerator : IDmxDeviceEnumerator
    {
        private readonly IEnttecDmxUsbDeviceEnumerator _enttecDmxUsbDeviceEnumerator;
        private readonly IArtNetController _artNetController;
        private readonly IPixelMapSource _pixelMapSource;

        public DmxDeviceEnumerator(
            IEnttecDmxUsbDeviceEnumerator enttecDmxUsbDeviceEnumerator,
            IArtNetController artNetController,
            IPixelMapSource pixelMapSource)
        {
            _enttecDmxUsbDeviceEnumerator = enttecDmxUsbDeviceEnumerator;
            _artNetController = artNetController;
            _pixelMapSource = pixelMapSource;
        }

        // Venue history, kept for reference when authoring a map. These were the hardcoded lines;
        // the equivalent today is a fixture whose OutputTarget names the same endpoint.
        //
        //   new DdpDevice(new byte[] { 192, 168, 3, 216 }, 1440)
        //   new DdpDevice(new byte[] { 192, 168, 3, 208 }, 8640)
        //   new DdpDevice(new byte[] { 192, 168, 3, 93 }, 8640)
        //   new DdpDevice(new byte[] { 192, 168, 3, 103 }, 8640)
        //   new DdpDevice(new byte[] { 192, 168, 3, 207 }, 8640)
        //   new ArtNetDevice(_artNetController, 0..3,  new byte[] { 192, 168, 3, 224 }, 510/510/510/270)
        //   new ArtNetDevice(_artNetController, 0..2,  new byte[] { 192, 168, 3, 209 }, 510/510/60)
        //   new ArtNetDevice(_artNetController, 16..18,new byte[] { 192, 168, 3, 209 }, 510/510/60)
        //   new ArtNetDevice(_artNetController, 0..4,  new byte[] { 192, 168, 3, 207 }, 510x4/120)
        //   new ArtNetDevice(_artNetController, 5..18, new byte[] { 192, 168, 3, 207 }, 510x13/60)
        //   new ArtNetDevice(_artNetController, 0..8,  new byte[] { 192, 168, 3, 196 }, 510x8/30)
        public IDmxOutputDevice[] GetDmxDevices(CancellationToken token)
        {
            var layout = _pixelMapSource.CurrentLayout;
            var devices = new IDmxOutputDevice[layout.Outputs.Count];

            for (int i = 0; i < devices.Length; i++)
            {
                var segment = layout.Outputs[i];
                var address = ParseIpAddress(segment.Target.IpAddress);

                devices[i] = segment.Target.Interface switch
                {
                    OutputInterfaceType.ArtNet => new ArtNetDevice(
                        _artNetController,
                        segment.Target.Universe,
                        address,
                        segment.ChannelCount),
                    OutputInterfaceType.Ddp => new DdpDevice(
                        address,
                        segment.ChannelCount,
                        port: segment.Target.Port),
                    _ => throw new NotSupportedException($"Unsupported output interface '{segment.Target.Interface}'.")
                };
            }

            return devices;
        }

        private static byte[] ParseIpAddress(string ipAddress)
        {
            if (!IPAddress.TryParse(ipAddress, out var parsed))
            {
                throw new ArgumentException($"'{ipAddress}' is not a valid IP address.", nameof(ipAddress));
            }
            return parsed.GetAddressBytes();
        }
    }
}
