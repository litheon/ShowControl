using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ShowControl.Devices.Output
{
    public class EnttecDmxUsbDeviceEnumerator : Ftdi, IEnttecDmxUsbDeviceEnumerator
    {
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_ListDevices(IntPtr pvArg1, IntPtr pvArg2, UInt32 flags);

        public IEnttecDmxUsbDevice[] GetDmxOutputDevices(CancellationToken cancellationToken)
        {
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(int));
            HandleFTDIOperation(FT_ListDevices(ptr, IntPtr.Zero, (uint)FT_LIST.NUMBER_ONLY), nameof(FT_ListDevices));
            int[] numDevicesArray = new int[1];
            Marshal.Copy(ptr, numDevicesArray, 0, numDevicesArray.Length);
            Marshal.FreeHGlobal(ptr);
            int numDevices = numDevicesArray[0];
            IEnttecDmxUsbDevice[] devices = new IEnttecDmxUsbDevice[numDevices];
            for (int i = 0; i < numDevices; i++)
            {
                devices[i] = new EnttecDmxUsbDevice(Convert.ToUInt32(i), cancellationToken);
            } 
            return devices;
        }

        [Flags]
        private enum FT_LIST : uint
        {
            NUMBER_ONLY = 0x80000000,
            BY_INDEX = 0x40000000,
            ALL = 0x20000000
        };
    }
}
