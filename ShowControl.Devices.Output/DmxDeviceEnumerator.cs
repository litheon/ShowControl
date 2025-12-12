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
            //return new IDmxOutputDevice[] { new ArtNetDevice(0, new byte[] { 192, 168, 3, 109 }), new ArtNetDevice(1, new byte[] { 192, 168, 3, 109 }), new ArtNetDevice(0, new byte[] { 192, 168, 3, 234 }), new ArtNetDevice(1, new byte[] { 192, 168, 3, 234 }), new ArtNetDevice(2, new byte[] { 192, 168, 3, 234 }) };

            return _enttecDmxUsbDeviceEnumerator.GetDmxOutputDevices(token);
        }
    }
}
