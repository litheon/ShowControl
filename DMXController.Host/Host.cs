using System;
using System.Threading.Tasks;
using ShowControl.Devices.Output;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading;
using ShowControl.Rendering;
using ShowControl.Devices.Input;
using ShowControl.Devices.Audio;
using NAudio.Dsp;
using ShowControl.Devices.DMX;

namespace ShowControl.Host
{
    public class Host
    {
        [STAThread]
        public static Task Main(string[] args)
        {

            var builder = CreateHostBuilder(args);
            return builder.RunConsoleAsync();
        }

        [STAThread]
        public static Task<IHost> Main(string[] args, EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler, Action<IDmxDevice[]> devices)
        {

            var builder = CreateHostBuilder(args, audioFrameProcessedEventHandler, devices);
            return builder.StartAsync();
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .ConfigureServices((_, services) =>
                    services
                        .AddSingleton<IEnttecDmxUsbDeviceEnumerator, EnttecDmxUsbDeviceEnumerator>()
                        .AddSingleton<IDmxDeviceEnumerator, DmxDeviceEnumerator>()
                        .AddSingleton<IInputService, InputService>()
                        .AddSingleton<IAudioService, AudioService>()
                        .AddHostedService<RenderService>());

        static IHostBuilder CreateHostBuilder(string[] args, EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler, Action<IDmxDevice[]> devices) =>
            Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .ConfigureServices((_, services) =>
                    services
                        .AddSingleton<IEnttecDmxUsbDeviceEnumerator, EnttecDmxUsbDeviceEnumerator>()
                        .AddSingleton<IDmxDeviceEnumerator, DmxDeviceEnumerator>()
                        .AddSingleton<IInputService, InputService>()
                        .AddSingleton<IAudioService, AudioService>((_) => new AudioService(audioFrameProcessedEventHandler))
                        .AddSingleton((_) => devices)
                        .AddHostedService<RenderService>());


    }
}
