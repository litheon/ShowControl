using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace ShowControl.Devices.Output
{
    public class DmxDeviceEnumerator : IDmxDeviceEnumerator
    {
        private readonly IEnttecDmxUsbDeviceEnumerator _enttecDmxUsbDeviceEnumerator;
        public DmxDeviceEnumerator(IEnttecDmxUsbDeviceEnumerator enttecDmxUsbDeviceEnumerator)
        {
            _enttecDmxUsbDeviceEnumerator = enttecDmxUsbDeviceEnumerator;
        }
        public IDmxOutputDevice[] GetDmxDevices(CancellationToken token)
        {
            return new IDmxOutputDevice[] {
                // new ArtNetDevice(0, new byte[] { 192, 168, 3, 216 }, 510),
                // new ArtNetDevice(1, new byte[] { 192, 168, 3, 216 }, 510),
                // new ArtNetDevice(2, new byte[] { 192, 168, 3, 216 }, 270),
                // new ArtNetDevice(6, new byte[] { 192, 168, 3, 209 }, 510),
                // new ArtNetDevice(7, new byte[] { 192, 168, 3, 209 }, 510),
                // new ArtNetDevice(8, new byte[] { 192, 168, 3, 209 }, 270),
                new DdpDevice(new byte[] { 192, 168, 3, 216 }, 1440),
                //new DdpDevice(new byte[] { 192, 168, 3, 224 }, 1800),
                new ArtNetDevice(0, new byte[] { 192, 168, 3, 224 }, 510),
                new ArtNetDevice(1, new byte[] { 192, 168, 3, 224 }, 510),
                new ArtNetDevice(2, new byte[] { 192, 168, 3, 224 }, 510),
                new ArtNetDevice(3, new byte[] { 192, 168, 3, 224 }, 270),

                new ArtNetDevice(0, new byte[] { 192, 168, 3, 209 }, 510),
                new ArtNetDevice(1, new byte[] { 192, 168, 3, 209 }, 510),
                new ArtNetDevice(2, new byte[] { 192, 168, 3, 209 }, 60),

                new ArtNetDevice(16, new byte[] { 192, 168, 3, 209 }, 510),
                new ArtNetDevice(17, new byte[] { 192, 168, 3, 209 }, 510),
                new ArtNetDevice(18, new byte[] { 192, 168, 3, 209 }, 60),
                
                new ArtNetDevice(0, new byte[] { 192, 168, 3, 196 }, 510),
                new ArtNetDevice(1, new byte[] { 192, 168, 3, 196 }, 510),
                new ArtNetDevice(2, new byte[] { 192, 168, 3, 196 }, 60),
                // new ArtNetDevice(2, new byte[] { 192, 168, 3, 216 }, 510),
                // new ArtNetDevice(3, new byte[] { 192, 168, 3, 216 }, 450),
                // new ArtNetDevice(9, new byte[] { 192, 168, 3, 216 }, 510),
                // new ArtNetDevice(10, new byte[] { 192, 168, 3, 216 }, 510),
                // new ArtNetDevice(11, new byte[] { 192, 168, 3, 216 }, 60),
            };

            return _enttecDmxUsbDeviceEnumerator.GetDmxOutputDevices(token);
        }
    }
}
