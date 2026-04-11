using Microsoft.Extensions.Logging;
using ShowControl.Graphics;
using System.Diagnostics;

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
            int x = 10;
            while (x > 0)
            {
                Thread.Sleep(1000);
                x--;
                if (x <= 5 && System.Diagnostics.Debugger.IsAttached)
                {
                    break;
                }
            }

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var message = $"Unhandled exception (IsTerminating={e.IsTerminating}): {e.ExceptionObject}";
                Debug.WriteLine(message);
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                var message = $"Unobserved task exception: {e.Exception}";
                Debug.WriteLine(message);
                e.SetObserved();
            };

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
                });

            builder.Logging.AddDebug();
#if !DEBUG
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
#endif

            return builder.Build();
        }
    }
}
