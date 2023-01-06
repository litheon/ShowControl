using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ShowControl.Devices.Output
{
    public class EnttecDmxUsbDevice : Ftdi, IEnttecDmxUsbDevice
    {
#if WIN64
        private UInt64 _handle = 0;
#elif WIN32
        private uint _handle = 0;
#endif
        public uint Id { get; private set; }

        private readonly Timer _ftdiFrameTimer;
        private readonly EventWaitHandle _waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        private readonly IntPtr _ftdiDmxBuffer;
        private const int BufferSize = 513;
        private readonly byte[] _dmxBuffer = new byte[BufferSize];
        private readonly Thread _writeThread;
        private long _writeLoopContinue = 1;
        private readonly CancellationToken _cancellationToken;
        private readonly AutoResetEvent _workWaitEvent;
        private readonly AutoResetEvent _frameWrittenEvent;
        internal EnttecDmxUsbDevice(uint deviceId, CancellationToken token)
        {
            Id = deviceId;
            _cancellationToken = token;
            HandleFTDIOperation(FT_Open(deviceId, ref _handle), nameof(FT_Open));
            HandleFTDIOperation(FT_ResetDevice(_handle), nameof(FT_ResetDevice));
            HandleFTDIOperation(FT_SetDivisor(_handle, (char)12), nameof(FT_SetDivisor));  // set baud rate
            HandleFTDIOperation(FT_SetDataCharacteristics(_handle, BITS_8, STOP_BITS_2, PARITY_NONE), nameof(FT_SetDataCharacteristics));
            HandleFTDIOperation(FT_SetFlowControl(_handle, (char)FLOW_NONE, 0, 0), nameof(FT_SetFlowControl));
            HandleFTDIOperation(FT_ClrRts(_handle), nameof(FT_ClrRts));
            HandleFTDIOperation(FT_Purge(_handle, PURGE_TX), nameof(FT_Purge));
            HandleFTDIOperation(FT_Purge(_handle, PURGE_RX), nameof(FT_Purge));
            _workWaitEvent = new AutoResetEvent(false);
            _frameWrittenEvent = new AutoResetEvent(false);
            _ftdiDmxBuffer = Marshal.AllocHGlobal(BufferSize);
            _writeThread = new Thread(WriteLoop);
            _writeThread.Start();
            _ftdiFrameTimer = new Timer(FtdiFrameTimerTick, null, 0, Timeout.Infinite);
        }

        ~EnttecDmxUsbDevice()
        {
            Interlocked.Exchange(ref _writeLoopContinue, 0);
            if (_handle != 0)
            {
                FT_Close(_handle);
                _handle = 0;
            }
            Marshal.FreeHGlobal(_ftdiDmxBuffer);
        }


        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_Open(uint uiPort, ref UInt64 ftHandle);
#elif WIN32
        private static extern FT_STATUS FT_Open(uint uiPort, ref uint ftHandle);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_ResetDevice(UInt64 ftHandle);
#elif WIN32
        private static extern FT_STATUS FT_ResetDevice(uint ftHandle);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_Close(UInt64 ftHandle);
#elif WIN32
        private static extern FT_STATUS FT_Close(uint ftHandle);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_Read(UInt64 ftHandle, IntPtr lpBuffer, UInt32 dwBytesToRead, ref UInt32 lpdwBytesReturned);
#elif WIN32
        private static extern FT_STATUS FT_Read(uint ftHandle, IntPtr lpBuffer, UInt32 dwBytesToRead, ref UInt32 lpdwBytesReturned);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_Write(UInt64 ftHandle, IntPtr lpBuffer, UInt32 dwBytesToRead, ref UInt32 lpdwBytesWritten);
#elif WIN32
        private static extern FT_STATUS FT_Write(uint ftHandle, IntPtr lpBuffer, UInt32 dwBytesToRead, ref UInt32 lpdwBytesWritten);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_SetDataCharacteristics(UInt64 ftHandle, byte uWordLength, byte uStopBits, byte uParity);
#elif WIN32
        private static extern FT_STATUS FT_SetDataCharacteristics(uint ftHandle, byte uWordLength, byte uStopBits, byte uParity);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_SetFlowControl(UInt64 ftHandle, char usFlowControl, byte uXon, byte uXoff);
#elif WIN32
        private static extern FT_STATUS FT_SetFlowControl(uint ftHandle, char usFlowControl, byte uXon, byte uXoff);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_GetModemStatus(UInt64 ftHandle, ref UInt32 lpdwModemStatus);
#elif WIN32
        private static extern FT_STATUS FT_GetModemStatus(uint ftHandle, ref UInt32 lpdwModemStatus);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_Purge(UInt64 ftHandle, UInt32 dwMask);
#elif WIN32
        private static extern FT_STATUS FT_Purge(uint ftHandle, UInt32 dwMask);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_ClrRts(UInt64 ftHandle);
#elif WIN32
        private static extern FT_STATUS FT_ClrRts(uint ftHandle);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_SetBreakOn(UInt64 ftHandle);
#elif WIN32
        private static extern FT_STATUS FT_SetBreakOn(uint ftHandle);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_SetBreakOff(UInt64 ftHandle);
#elif WIN32
        private static extern FT_STATUS FT_SetBreakOff(uint ftHandle);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_GetStatus(UInt64 ftHandle, ref UInt32 lpdwAmountInRxQueue, ref UInt32 lpdwAmountInTxQueue, ref UInt32 lpdwEventStatus);
#elif WIN32
        private static extern FT_STATUS FT_GetStatus(uint ftHandle, ref UInt32 lpdwAmountInRxQueue, ref UInt32 lpdwAmountInTxQueue, ref UInt32 lpdwEventStatus);
#endif

        [DllImport("FTD2XX.dll")]
#if WIN64
        private static extern FT_STATUS FT_SetDivisor(UInt64 ftHandle, char usDivisor);
#elif WIN32
        private static extern FT_STATUS FT_SetDivisor(uint ftHandle, char usDivisor);
#endif

        private const byte BITS_8 = 8;
        private const byte STOP_BITS_2 = 2;
        private const byte PARITY_NONE = 0;
        private const UInt16 FLOW_NONE = 0;
        private const byte PURGE_RX = 1;
        private const byte PURGE_TX = 2;

        private const int MinimumDelayBetweenFtdiFrameRenders = 20;

        private long _writeLoopLastTicks;
        private const int MinimumMillisecondsBetweenFrames = 3;
        private void WriteLoop()
        {
            while(Interlocked.Read(ref _writeLoopContinue) == 1 && !_cancellationToken.IsCancellationRequested)
            {
                _waitHandle.WaitOne();
                _ftdiFrameTimer.Change(Timeout.Infinite, Timeout.Infinite);
                var now = DateTime.Now.Ticks;
                var delta = now - _writeLoopLastTicks;
                if (delta < MinimumMillisecondsBetweenFrames * TimeSpan.TicksPerMillisecond)
                {
                    var waitTimeSpan = Math.Ceiling(((double)MinimumMillisecondsBetweenFrames * TimeSpan.TicksPerMillisecond - delta) / TimeSpan.TicksPerMillisecond);
                    Thread.Sleep(Convert.ToInt32(waitTimeSpan));
                }
                uint bytesWritten = 0;
                lock (_dmxBuffer)
                {
                    Marshal.Copy(_dmxBuffer, 0, _ftdiDmxBuffer, _dmxBuffer.Length);
                }
                HandleFTDIOperation(FT_SetBreakOn(_handle), nameof(FT_SetBreakOn));
                HandleFTDIOperation(FT_SetBreakOff(_handle), nameof(FT_SetBreakOff));
                HandleFTDIOperation(FT_Write(_handle, _ftdiDmxBuffer, BufferSize, ref bytesWritten), nameof(FT_Write));
                _waitHandle.Reset();
                _ftdiFrameTimer.Change(MinimumDelayBetweenFtdiFrameRenders, Timeout.Infinite);
                _writeLoopLastTicks = DateTime.Now.Ticks;
                _frameWrittenEvent.Set();
            }
        }

        private void FtdiFrameTimerTick(object state)
        {
            _waitHandle.Set();
        }

        public void Write(byte[] dmxData)
        {
            if (dmxData.Length > BufferSize - 1)
            {
                throw new ArgumentException("Buffer size is too large");
            }
            byte[] lastBuffer = new byte[BufferSize];
            lock (_dmxBuffer)
            {
                Array.Copy(_dmxBuffer, 1, lastBuffer, 0, _dmxBuffer.Length - 1);
                for (int i = 0; i < dmxData.Length; i++)
                {
                    _dmxBuffer[i + 1] = dmxData[i];
                }
            }

            for(int i = 0; i < dmxData.Length; i++)
            {
                if (lastBuffer[i] != dmxData[i])
                {
                    _waitHandle.Set();
                    _frameWrittenEvent.WaitOne();
                    break;
                }
            }
        }
    }
}
