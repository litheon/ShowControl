using Microsoft.Extensions.Logging;
using ShowControl.Graphics;

#if MACCATALYST
using ShowControl.ControlPanel.Maui.Platforms.MacCatalyst;
#endif

namespace ShowControl.ControlPanel.Maui
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
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
#endif
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
