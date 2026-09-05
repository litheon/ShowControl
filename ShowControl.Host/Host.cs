using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ShowControl.Devices.Output;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using ShowControl.Rendering;
using ShowControl.Devices.Input;
using ShowControl.Devices.Audio;
using NAudio.Dsp;
using ShowControl.Devices.DMX;
using ShowControl.PixelMapping;
using Serilog;
using ShowControl.Graphics;

namespace ShowControl.Host
{
    public class Host
    {
        [STAThread]
        public static Task<IHost> Main(string[] args, EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler, Action<PixelMapSummary> pixelMap)
        {
            LoggingBootstrap.Initialize();
            var builder = CreateHostBuilder(args, audioFrameProcessedEventHandler, pixelMap);
            return builder.StartAsync();
        }

        static IHostBuilder CreateHostBuilder(string[] args, EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler, Action<PixelMapSummary> pixelMap) =>
            Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices((_, services) =>
                    services
                        // The pixel map is resolved before anything that renders: both the device
                        // enumerator and RenderService are sized by it, so it has to be loaded
                        // synchronously rather than awaited into place later.
                        .AddSingleton<PixelMapSession>(sp => PixelMapSession.LoadAtStartup(
                            PixelTypeRegistry.Default,
                            sp.GetRequiredService<ILogger<PixelMapSession>>()))
                        .AddSingleton<IPixelMapSource>(sp => sp.GetRequiredService<PixelMapSession>())
                        .AddSingleton<IPixelMapEditor>(sp => sp.GetRequiredService<PixelMapSession>())
                        .AddSingleton<IPixelTypeResolver>(_ => PixelTypeRegistry.Default)
                        .AddSingleton<IEnttecDmxUsbDeviceEnumerator, EnttecDmxUsbDeviceEnumerator>()
                        .AddSingleton<IDmxDeviceEnumerator, DmxDeviceEnumerator>()
                        .AddSingleton<IInputService, InputService>()
                        // The analysis engine runs for the life of the app, independent of which
                        // visualization is on screen: it is owned here, fed by the capture callback,
                        // and never started or stopped by an animation or shader.
                        //
                        // The beat metadata source is the synthetic one until the native Pro DJ
                        // Link port exists — it reports Absent until the debug panel arms a grid,
                        // so the PLL runs locally by default. The port replaces this registration.
                        .AddSingleton<ShowControl.AudioAnalysis.SyntheticBeatMetadataSource>()
                        .AddSingleton<ShowControl.AudioAnalysis.IBeatMetadataSource>(sp =>
                            sp.GetRequiredService<ShowControl.AudioAnalysis.SyntheticBeatMetadataSource>())
                        .AddSingleton(sp => new ShowControl.AudioAnalysis.AudioAnalysisEngine(
                            beatMetadata: sp.GetRequiredService<ShowControl.AudioAnalysis.IBeatMetadataSource>()))
                        .AddSingleton<IAudioService, AudioService>((sp) => new AudioService(
                            sp.GetRequiredService<ILogger<AudioService>>(),
                            audioFrameProcessedEventHandler,
                            sp.GetRequiredService<ShowControl.AudioAnalysis.AudioAnalysisEngine>()))
                        .AddSingleton<IGraphicsHost, GraphicsHost>()
                        .AddSingleton<IArtNetController, ArtNetController>()
                        .AddSingleton((_) => pixelMap)
                        .AddLogging()
                        .AddHostedService<RenderService>());


    }
}
