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
        public static async Task Main(string[] args)
        {
            RegisterGlobalExceptionHandlers();

            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug() // Set the minimum level to Debug
            .WriteTo.Debug()
            .CreateLogger();


            //System.Threading.Thread.Sleep(10000);
            var builder = CreateHostBuilder(args);
            await builder.RunConsoleAsync();
        }

        [STAThread]
        public static Task<IHost> Main(string[] args, EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler, Action<IDmxDevice[]> devices)
        {

            var builder = CreateHostBuilder(args, audioFrameProcessedEventHandler, devices);
            return builder.StartAsync();
        }

        private static void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var message = $"Unhandled exception (IsTerminating={e.IsTerminating}): {e.ExceptionObject}";
                Debug.WriteLine(message);
                Console.Error.WriteLine(message);
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                var message = $"Unobserved task exception: {e.Exception}";
                Debug.WriteLine(message);
                Console.Error.WriteLine(message);
                e.SetObserved();
            };
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .ConfigureServices((_, services) =>
                    services
                        .AddSingleton<IEnttecDmxUsbDeviceEnumerator, EnttecDmxUsbDeviceEnumerator>()
                        .AddSingleton<IDmxDeviceEnumerator, DmxDeviceEnumerator>()
                        .AddSingleton<IInputService, InputService>()
                        .AddSingleton<IAudioService, AudioService>()
                        .AddSingleton<IGraphicsHost, GraphicsHost>()
                        .AddSingleton<IArtNetController, ArtNetController>()
                        .AddLogging()
                        .AddHostedService<RenderService>());

        static IHostBuilder CreateHostBuilder(string[] args, EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler, Action<IDmxDevice[]> devices) =>
            Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
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
