using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Serilog;
using ShowControl.Graphics;

#if MACCATALYST
using ShowControl.ControlPanel.Maui.Platforms.MacCatalyst;
#elif WINDOWS
using ShowControl.ControlPanel.Maui.Platforms.Windows;
#endif

namespace ShowControl.ControlPanel.Maui
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            //int x = 10;
            //while (x > 0)
            //{
            //    Thread.Sleep(1000);
            //    x--;
            //    if (x <= 5 && System.Diagnostics.Debugger.IsAttached)
            //    {
            //        break;
            //    }
            //}

            ShowControl.Host.LoggingBootstrap.Initialize();

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                })
                .ConfigureMauiHandlers(handlers =>
                {
#if MACCATALYST
                    handlers.AddHandler<MetalSurfaceView, MetalSurfaceHandler>();
                    handlers.AddHandler<VulkanSurfaceView, VulkanSurfaceHandler>();
#elif WINDOWS
                    // Use SwapChainPanelHandler for DirectComposition (enables XAML overlays on Vulkan content)
                    handlers.AddHandler<VulkanSurfaceView, SwapChainPanelHandler>();
                    // Alternatively, use Win32SurfaceHandler for direct Vulkan presentation (no XAML overlay support):
                    //handlers.AddHandler<VulkanSurfaceView, Win32SurfaceHandler>();
#endif
                })
                .ConfigureLifecycleEvents(ConfigureShutdown);

            builder.Logging.AddDebug();
            builder.Logging.AddSerilog();
#if !DEBUG
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
#endif

            return builder.Build();
        }

        /// <summary>
        /// Exit handling.
        /// <para>
        /// The working copy is always flushed, on every path out. Beyond that the platforms differ:
        /// WinUI's <c>AppWindow.Closing</c> is cancellable, so Windows gets a real "save before
        /// quitting?" prompt. Mac Catalyst under the sandbox has no terminate hook that can veto a
        /// quit, so it flushes on the way out and asks on the next launch instead — which is why
        /// the working copy has to be authoritative rather than advisory.
        /// </para>
        /// </summary>
        private static void ConfigureShutdown(ILifecycleBuilder lifecycle)
        {
#if MACCATALYST
            lifecycle.AddiOS(ios => ios
                .WillTerminate(_ => AppLifetime.Shutdown(TimeSpan.FromSeconds(2)))
                .OnResignActivation(_ => AppLifetime.FlushPixelMap()));
#elif WINDOWS
            lifecycle.AddWindows(windows => windows.OnWindowCreated(window =>
            {
                if (window is not Microsoft.UI.Xaml.Window platformWindow)
                {
                    return;
                }

                // Via the window handle rather than a MAUI extension method: this is the canonical
                // WinUI 3 route to the AppWindow and does not depend on MAUI internals.
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                if (appWindow == null)
                {
                    return;
                }

                appWindow.Closing += async (_, args) =>
                {
                    // Nothing is lost either way — the flush has already happened — but on a
                    // platform that lets us ask, ask.
                    if (!AppLifetime.HasUnsavedPixelMap)
                    {
                        return;
                    }

                    args.Cancel = true;
                    AppLifetime.FlushPixelMap();

                    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                    string choice = page == null
                        ? "Discard"
                        : await page.DisplayActionSheet(
                            "The pixel map has unsaved changes.",
                            cancel: "Keep working",
                            destruction: "Discard and quit",
                            "Save and quit");

                    if (choice == "Keep working" || choice == null)
                    {
                        return;
                    }

                    if (choice == "Save and quit" && !string.IsNullOrEmpty(AppLifetime.PixelMapPath))
                    {
                        AppLifetime.SavePixelMapTo(AppLifetime.PixelMapPath);
                    }

                    AppLifetime.Shutdown(TimeSpan.FromSeconds(2));
                    Application.Current?.Quit();
                };
            }));
#endif
        }
    }
}
