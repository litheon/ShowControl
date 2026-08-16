using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShowControl.PixelMapping;
using System;
using System.Threading;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// The few pieces of app-lifetime state that shutdown needs to reach from outside a page.
    /// <para>
    /// Deliberately narrow rather than a general service locator: pages resolve their own
    /// dependencies through the host they were handed. This exists because window teardown happens
    /// in <see cref="App"/> and platform lifecycle hooks, neither of which has a page to ask.
    /// </para>
    /// </summary>
    internal static class AppLifetime
    {
        private static IHost _host;
        private static IPixelMapEditor _pixelMapEditor;
        private static int _shutdownStarted;

        public static void Register(IHost host)
        {
            _host = host;
            _pixelMapEditor = host.Services.GetService<IPixelMapEditor>();
        }

        public static bool HasUnsavedPixelMap => _pixelMapEditor?.IsDirty == true;

        public static string PixelMapPath => _pixelMapEditor?.SavedPath;

        /// <summary>
        /// Writes the working copy now, synchronously.
        /// <para>
        /// This is the guarantee the whole exit story rests on: no platform here offers a
        /// cancellable terminate hook that can be relied on, so unsaved edits must already be on
        /// disk by the time anything asks the user about them. On the next launch they are
        /// recovered and the user is asked then.
        /// </para>
        /// </summary>
        public static void FlushPixelMap()
        {
            try
            {
                _pixelMapEditor?.SaveWorkingCopy();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[AppLifetime] Could not flush the pixel map working copy on exit.");
            }
        }

        public static void SavePixelMapTo(string path) => _pixelMapEditor?.Save(path);

        /// <summary>
        /// Flushes the map, then stops the host within <paramref name="timeout"/>.
        /// <para>
        /// Bounded because the callers are not awaited — <c>Window.Destroying</c> and
        /// <c>WillTerminate</c> both return void and the process can die mid-await. The flush runs
        /// first and synchronously so it completes even if the host stop does not.
        /// </para>
        /// </summary>
        public static void Shutdown(TimeSpan timeout)
        {
            if (Interlocked.Exchange(ref _shutdownStarted, 1) == 1)
            {
                return;
            }

            FlushPixelMap();

            var host = _host;
            if (host == null)
            {
                return;
            }

            try
            {
                if (!host.StopAsync().Wait(timeout))
                {
                    Serilog.Log.Warning("[AppLifetime] Host did not stop within {Timeout}; exiting anyway.", timeout);
                }
                host.Dispose();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[AppLifetime] Error stopping the host.");
            }
            finally
            {
                Serilog.Log.CloseAndFlush();
            }
        }
    }
}
