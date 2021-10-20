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
        public static Task Main(string[] args, Action<object, Complex[]> fftCallback)
        {

            var builder = CreateHostBuilder(args, fftCallback);
            return builder.RunConsoleAsync();
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

        static IHostBuilder CreateHostBuilder(string[] args, Action<object, Complex[]> fftCallback) =>
            Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .ConfigureServices((_, services) =>
                    services
                        .AddSingleton<IEnttecDmxUsbDeviceEnumerator, EnttecDmxUsbDeviceEnumerator>()
                        .AddSingleton<IDmxDeviceEnumerator, DmxDeviceEnumerator>()
                        .AddSingleton<IInputService, InputService>()
                        .AddSingleton<IAudioService, AudioService>((_) => new AudioService(fftCallback))
                        .AddHostedService<RenderService>());


    }
}
