using System;
using System.Threading;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// Measures main-thread responsiveness so UI lockups stop being anecdotes: a background
    /// thread pings the main queue and logs when the round-trip is slow (the main thread was
    /// busy or blocked). One ping is outstanding at a time, so a hung main thread accumulates
    /// no backlog; an ongoing hang is escalated with its running duration.
    /// </summary>
    internal static class MainThreadWatchdog
    {
        private static readonly TimeSpan PingInterval = TimeSpan.FromMilliseconds(500);
        private const long ReportThresholdMs = 300;
        private const long OngoingHangThresholdMs = 2000;

        /// <summary>Send timestamp of the outstanding ping; 0 while none is in flight.</summary>
        private static long _pending;
        private static int _started;

        public static void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                return;
            }
            new Thread(Loop) { IsBackground = true, Name = "MainThreadWatchdog" }.Start();
        }

        private static void Loop()
        {
            long worstReported = 0;
            while (true)
            {
                var sent = Environment.TickCount64;
                Volatile.Write(ref _pending, sent);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var delay = Environment.TickCount64 - sent;
                    Volatile.Write(ref _pending, 0);
                    if (delay > ReportThresholdMs)
                    {
                        Serilog.Log.Information(
                            "[MainThreadWatchdog] Main thread was unresponsive for {Delay}ms.", delay);
                    }
                });

                Thread.Sleep(PingInterval);

                // Still pending: the main thread is hung right now. Escalate as it grows, once
                // per new worst duration, so a long freeze reads as a story, not spam.
                while (Volatile.Read(ref _pending) != 0)
                {
                    var stuck = Environment.TickCount64 - Volatile.Read(ref _pending);
                    if (stuck > OngoingHangThresholdMs && stuck > worstReported * 2)
                    {
                        worstReported = stuck;
                        Serilog.Log.Warning(
                            "[MainThreadWatchdog] Main thread has been unresponsive for {Stuck}ms and counting.", stuck);
                    }
                    Thread.Sleep(100);
                }
                worstReported = 0;
            }
        }
    }
}
