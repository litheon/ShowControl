using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

namespace ShowControl.Host
{
    /// <summary>
    /// One-time Serilog setup shared by every entry point (console shell, MAUI app).
    /// Logs to Debug, Console, and a rolling file so failures at a venue leave a
    /// trace on disk. The file lands in the per-user app-data folder, which resolves
    /// inside the sandbox container on Mac Catalyst.
    /// </summary>
    public static class LoggingBootstrap
    {
        private static bool _initialized;
        private static readonly object _lock = new object();

        public static string LogDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "ShowControl", "logs");

        public static void Initialize()
        {
            lock (_lock)
            {
                if (_initialized) return;
                _initialized = true;

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                    .WriteTo.Debug()
                    .WriteTo.Console()
                    .WriteTo.Sink(UiLogSink.Instance)
                    .WriteTo.File(Path.Combine(LogDirectory, "showcontrol-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        fileSizeLimitBytes: 50_000_000,
                        rollOnFileSizeLimit: true,
                        flushToDiskInterval: TimeSpan.FromSeconds(2))
                    .CreateLogger();

                Log.Information("=== ShowControl started, pid {Pid}, logs at {LogDirectory} ===",
                    Environment.ProcessId, LogDirectory);

                RegisterGlobalExceptionHandlers();
            }
        }

        private static void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Log.Fatal("Unhandled exception (IsTerminating={IsTerminating}): {Exception}",
                    e.IsTerminating, e.ExceptionObject);
                Log.CloseAndFlush();
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Log.Error(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, e) => Log.CloseAndFlush();
        }
    }
}
