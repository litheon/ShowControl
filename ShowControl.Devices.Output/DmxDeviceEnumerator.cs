using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace ShowControl.Devices.Output
{
    public class DmxDeviceEnumerator : IDmxDeviceEnumerator
    {
        private readonly IEnttecDmxUsbDeviceEnumerator _enttecDmxUsbDeviceEnumerator;
        private readonly IArtNetController _artNetController;

        public DmxDeviceEnumerator(IEnttecDmxUsbDeviceEnumerator enttecDmxUsbDeviceEnumerator, IArtNetController artNetController)
        {
            _enttecDmxUsbDeviceEnumerator = enttecDmxUsbDeviceEnumerator;
            _artNetController = artNetController;
        }
        public IDmxOutputDevice[] GetDmxDevices(CancellationToken token)
        {
            return new IDmxOutputDevice[] {
                // new DdpDevice(new byte[] { 192, 168, 3, 216 }, 1440),

                // new ArtNetDevice(_artNetController,0, new byte[] { 192, 168, 3, 224 }, 510),
                // new ArtNetDevice(_artNetController,1, new byte[] { 192, 168, 3, 224 }, 510),
                // new ArtNetDevice(_artNetController,2, new byte[] { 192, 168, 3, 224 }, 510),
                // new ArtNetDevice(_artNetController,3, new byte[] { 192, 168, 3, 224 }, 270),

                // new ArtNetDevice(_artNetController,0, new byte[] { 192, 168, 3, 209 }, 510),
                // new ArtNetDevice(_artNetController,1, new byte[] { 192, 168, 3, 209 }, 510),
                // new ArtNetDevice(_artNetController,2, new byte[] { 192, 168, 3, 209 }, 60),

                // new ArtNetDevice(_artNetController,16, new byte[] { 192, 168, 3, 209 }, 510),
                // new ArtNetDevice(_artNetController,17, new byte[] { 192, 168, 3, 209 }, 510),
                // new ArtNetDevice(_artNetController,18, new byte[] { 192, 168, 3, 209 }, 60),
                
                new ArtNetDevice(_artNetController,0, new byte[] { 192, 168, 3, 93 }, 510),
                new ArtNetDevice(_artNetController,1, new byte[] { 192, 168, 3, 93 }, 510),
                new ArtNetDevice(_artNetController,2, new byte[] { 192, 168, 3, 93 }, 510),
                new ArtNetDevice(_artNetController,3, new byte[] { 192, 168, 3, 93 }, 510),
                new ArtNetDevice(_artNetController,4, new byte[] { 192, 168, 3, 93 }, 510),
                new ArtNetDevice(_artNetController,5, new byte[] { 192, 168, 3, 93 }, 510),
                new ArtNetDevice(_artNetController,6, new byte[] { 192, 168, 3, 93 }, 510),
                new ArtNetDevice(_artNetController,7, new byte[] { 192, 168, 3, 93 }, 510),
                new ArtNetDevice(_artNetController,8, new byte[] { 192, 168, 3, 93 }, 240),

                // new ArtNetDevice(_artNetController,0, new byte[] { 192, 168, 3, 196 }, 510),
                // new ArtNetDevice(_artNetController,1, new byte[] { 192, 168, 3, 196 }, 510),
                // new ArtNetDevice(_artNetController,2, new byte[] { 192, 168, 3, 196 }, 510),
                // new ArtNetDevice(_artNetController,3, new byte[] { 192, 168, 3, 196 }, 510),
                // new ArtNetDevice(_artNetController,4, new byte[] { 192, 168, 3, 196 }, 510),
                // new ArtNetDevice(_artNetController,5, new byte[] { 192, 168, 3, 196 }, 510),
                // new ArtNetDevice(_artNetController,6, new byte[] { 192, 168, 3, 196 }, 510),
                // new ArtNetDevice(_artNetController,7, new byte[] { 192, 168, 3, 196 }, 510),
                // new ArtNetDevice(_artNetController,8, new byte[] { 192, 168, 3, 196 }, 30),
            };

            return _enttecDmxUsbDeviceEnumerator.GetDmxOutputDevices(token);
        }
    }
}
