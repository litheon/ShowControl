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
                        // The beat metadata source prefers the native Pro DJ Link port
                        // (BeatLinkNet) whenever CDJs are on the network, and falls back to the
                        // synthetic source so the debug panel's Grid button still exercises the
                        // PLL with no gear present. The local clock is resolved lazily through
                        // the provider because the engine (whose sample clock it is) takes this
                        // source in its own constructor.
                        .AddSingleton<ShowControl.AudioAnalysis.SyntheticBeatMetadataSource>()
                        .AddSingleton(sp => new ShowControl.Devices.ProDjLink.ProDjLinkBeatMetadataSource(
                            localClock: () => sp.GetRequiredService<ShowControl.AudioAnalysis.AudioAnalysisEngine>().Latest.Time,
                            logger: sp.GetRequiredService<ILogger<ShowControl.Devices.ProDjLink.ProDjLinkBeatMetadataSource>>()))
                        .AddSingleton<ShowControl.AudioAnalysis.IBeatMetadataSource>(sp =>
                            new ShowControl.Devices.ProDjLink.PreferringBeatMetadataSource(
                                sp.GetRequiredService<ShowControl.Devices.ProDjLink.ProDjLinkBeatMetadataSource>(),
                                sp.GetRequiredService<ShowControl.AudioAnalysis.SyntheticBeatMetadataSource>()))
                        .AddHostedService<ShowControl.Devices.ProDjLink.ProDjLinkService>()
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
