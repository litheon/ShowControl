using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
using ShowControl.Host;

namespace ShowControl
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
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

            Host.Host.Main(new string[] { }, OnAudioFrameProcessed, HandleDevices).Wait();
        }

        private static void OnAudioFrameProcessed(object sender, AudioFrameProcessedEventArgs e)
        {
            Console.WriteLine(e.Bpm.ToString());
        }

        private static void HandleDevices(IDmxDevice[] devices) 
        {

        }
    }
}
