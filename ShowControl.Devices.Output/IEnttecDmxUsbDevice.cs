using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Devices.Output
{
    public interface IEnttecDmxUsbDevice : IDmxOutputDevice
    {
        public uint Id { get; }
    }
}
