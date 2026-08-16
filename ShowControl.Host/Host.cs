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
using Serilog;
using ShowControl.Graphics;

namespace ShowControl.Host
{
    public class Host
    {
        [STAThread]
        public static Task<IHost> Main(string[] args, EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler, Action<IDmxDevice[]> devices)
        {
            LoggingBootstrap.Initialize();
            var builder = CreateHostBuilder(args, audioFrameProcessedEventHandler, devices);
            return builder.StartAsync();
        }

        static IHostBuilder CreateHostBuilder(string[] args, EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler, Action<IDmxDevice[]> devices) =>
            Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices((_, services) =>
                    services
                        .AddSingleton<IEnttecDmxUsbDeviceEnumerator, EnttecDmxUsbDeviceEnumerator>()
                        .AddSingleton<IDmxDeviceEnumerator, DmxDeviceEnumerator>()
                        .AddSingleton<IInputService, InputService>()
                        .AddSingleton<IAudioService, AudioService>((sp) => new AudioService(sp.GetRequiredService<ILogger<AudioService>>(), audioFrameProcessedEventHandler))
                        .AddSingleton<IGraphicsHost, GraphicsHost>()
                        .AddSingleton<IArtNetController, ArtNetController>()
                        .AddSingleton((_) => devices)
                        .AddLogging()
                        .AddHostedService<RenderService>());


    }
}
