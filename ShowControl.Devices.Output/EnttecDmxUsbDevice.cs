using System;
using System.Buffers;
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
#elif ARM64
        private UInt64 _handle = 0;
#endif
        public int MaxChannels => 512;

        public uint Id { get; private set; }

        private readonly Timer _ftdiFrameTimer;
        private readonly EventWaitHandle _waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        private readonly IntPtr _ftdiProPacketHeaderBuffer;
        private readonly IntPtr _ftdiProPacketEndBuffer;
        private readonly IntPtr _ftdiDmxBuffer;
        private const byte ProPacketHeaderSize = 4;
        private const byte ProPacketDmxLabelNumber = 6;
        private const byte ProPacketDmxStartCode = 0x7E;
        private const byte ProPacketEndCode = 0xE7;
        private const int BufferSize = 513;
        private readonly byte[] _dmxBuffer = new byte[BufferSize];
        private readonly Thread _writeThread;
        private long _writeLoopContinue = 1;
        private readonly CancellationToken _cancellationToken;
        private readonly AutoResetEvent _workWaitEvent;
        private readonly AutoResetEvent _frameWrittenEvent;
        private ArrayPool<byte> _dmxBufferPool = ArrayPool<byte>.Shared;
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
            HandleFTDIOperation(FT_SetTimeouts(_handle, 10, 100), nameof(FT_SetTimeouts));
            HandleFTDIOperation(FT_Purge(_handle, PURGE_TX), nameof(FT_Purge));
            HandleFTDIOperation(FT_Purge(_handle, PURGE_RX), nameof(FT_Purge));
            _workWaitEvent = new AutoResetEvent(false);
            _frameWrittenEvent = new AutoResetEvent(false);
            _ftdiDmxBuffer = Marshal.AllocHGlobal(BufferSize);
            _ftdiProPacketHeaderBuffer = Marshal.AllocHGlobal(ProPacketHeaderSize);
            _ftdiProPacketEndBuffer = Marshal.AllocHGlobal(1);
            Marshal.Copy(new byte[] { ProPacketEndCode }, 0, _ftdiProPacketEndBuffer, 1);
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


#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Open(uint uiPort, ref UInt64 ftHandle);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Open(uint uiPort, ref uint ftHandle);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_Open(uint uiPort, ref UInt64 ftHandle);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_ResetDevice(UInt64 ftHandle);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_ResetDevice(uint ftHandle);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_ResetDevice(UInt64 ftHandle);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Close(UInt64 ftHandle);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Close(uint ftHandle);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_Close(UInt64 ftHandle);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Read(UInt64 ftHandle, IntPtr lpBuffer, UInt32 dwBytesToRead, ref UInt32 lpdwBytesReturned);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Read(uint ftHandle, IntPtr lpBuffer, UInt32 dwBytesToRead, ref UInt32 lpdwBytesReturned);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_Read(UInt64 ftHandle, IntPtr lpBuffer, UInt32 dwBytesToRead, ref UInt32 lpdwBytesReturned);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Write(UInt64 ftHandle, IntPtr lpBuffer, UInt32 dwBytesToRead, ref UInt32 lpdwBytesWritten);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Write(uint ftHandle, IntPtr lpBuffer, UInt32 dwBytesToRead, ref UInt32 lpdwBytesWritten);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_Write(UInt64 ftHandle, IntPtr lpBuffer, UInt32 dwBytesToRead, ref UInt32 lpdwBytesWritten);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetDataCharacteristics(UInt64 ftHandle, byte uWordLength, byte uStopBits, byte uParity);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetDataCharacteristics(uint ftHandle, byte uWordLength, byte uStopBits, byte uParity);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_SetDataCharacteristics(UInt64 ftHandle, byte uWordLength, byte uStopBits, byte uParity);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetFlowControl(UInt64 ftHandle, char usFlowControl, byte uXon, byte uXoff);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetFlowControl(uint ftHandle, char usFlowControl, byte uXon, byte uXoff);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_SetFlowControl(UInt64 ftHandle, char usFlowControl, byte uXon, byte uXoff);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_GetModemStatus(UInt64 ftHandle, ref UInt32 lpdwModemStatus);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_GetModemStatus(uint ftHandle, ref UInt32 lpdwModemStatus);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_GetModemStatus(UInt64 ftHandle, ref UInt32 lpdwModemStatus);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Purge(UInt64 ftHandle, UInt32 dwMask);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Purge(uint ftHandle, UInt32 dwMask);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_Purge(UInt64 ftHandle, UInt32 dwMask);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_ClrRts(UInt64 ftHandle);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_ClrRts(uint ftHandle);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_ClrRts(UInt64 ftHandle);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetBreakOn(UInt64 ftHandle);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetBreakOn(uint ftHandle);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_SetBreakOn(UInt64 ftHandle);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetBreakOff(UInt64 ftHandle);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetBreakOff(uint ftHandle);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_SetBreakOff(UInt64 ftHandle);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_GetStatus(UInt64 ftHandle, ref UInt32 lpdwAmountInRxQueue, ref UInt32 lpdwAmountInTxQueue, ref UInt32 lpdwEventStatus);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_GetStatus(uint ftHandle, ref UInt32 lpdwAmountInRxQueue, ref UInt32 lpdwAmountInTxQueue, ref UInt32 lpdwEventStatus);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_GetStatus(UInt64 ftHandle, ref UInt32 lpdwAmountInRxQueue, ref UInt32 lpdwAmountInTxQueue, ref UInt32 lpdwEventStatus);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetDivisor(UInt64 ftHandle, char usDivisor);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetDivisor(uint ftHandle, char usDivisor);
#elif ARM64
        [DllImport("ftd2xx")]
        private static extern FT_STATUS FT_SetDivisor(UInt64 ftHandle, char usDivisor);
#endif

#if WIN64
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetTimeouts(UInt64 ftHandle, UInt32 readTimeout, UInt32 writeTimeout);
#elif WIN32
        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetTimeouts(uint ftHandle, UInt32 readTimeout, UInt32 writeTimeout);
#elif ARM64
        [DllImport("libftd2xx")]
        private static extern FT_STATUS FT_SetTimeouts(UInt64 ftHandle, UInt32 readTimeout, UInt32 writeTimeout);
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
                var bytes = new byte[ProPacketHeaderSize] { ProPacketDmxStartCode, ProPacketDmxLabelNumber, (byte)(_dmxBuffer.Length & 0xFF), (byte)(_dmxBuffer.Length >> 8) };
                Marshal.Copy(bytes, 0, _ftdiProPacketHeaderBuffer, ProPacketHeaderSize);
                //HandleFTDIOperation(FT_SetBreakOn(_handle), nameof(FT_SetBreakOn));
                //HandleFTDIOperation(FT_SetBreakOff(_handle), nameof(FT_SetBreakOff));
                HandleFTDIOperation(FT_Write(_handle, _ftdiProPacketHeaderBuffer, ProPacketHeaderSize, ref bytesWritten), nameof(FT_Write));
                HandleFTDIOperation(FT_Write(_handle, _ftdiDmxBuffer, BufferSize, ref bytesWritten), nameof(FT_Write));
                HandleFTDIOperation(FT_Write(_handle, _ftdiProPacketEndBuffer, 1, ref bytesWritten), nameof(FT_Write));
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

        public Task Write(byte[] dmxData)
        {
            if (dmxData.Length > BufferSize - 1)
            {
                throw new ArgumentException("Buffer size is too large");
            }
            byte[] lastBuffer = _dmxBufferPool.Rent(BufferSize);
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
            _dmxBufferPool.Return(lastBuffer);
            return Task.CompletedTask;
        }
    }
}
