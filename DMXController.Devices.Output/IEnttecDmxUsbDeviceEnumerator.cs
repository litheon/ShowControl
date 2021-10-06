using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace ShowControl.Devices.Output
{
    public interface IEnttecDmxUsbDeviceEnumerator
    {
        public IEnttecDmxUsbDevice[] GetDmxOutputDevices(CancellationToken cancellationToken);
    }
}
