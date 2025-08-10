using System;
using System.Threading;
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
