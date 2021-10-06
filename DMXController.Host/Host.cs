using System;
using System.Threading.Tasks;
using ShowControl.Devices.Output;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading;
using ShowControl.Rendering;
using ShowControl.Devices.Input;

namespace ShowControl.Host
{
    public class Host
    {

        public static Task Main(string[] args)
        {
            var builder = CreateHostBuilder(args);
            return builder.RunConsoleAsync();
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .ConfigureServices((_, services) =>
                    services
                        .AddSingleton<IEnttecDmxUsbDeviceEnumerator, EnttecDmxUsbDeviceEnumerator>()
                        .AddSingleton<IDmxDeviceEnumerator, DmxDeviceEnumerator>()
                        .AddSingleton<IInputService, InputService>()
                        .AddHostedService<RenderService>());


    }
}
