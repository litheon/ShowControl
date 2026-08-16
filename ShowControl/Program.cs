using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
using ShowControl.Host;
using ShowControl.PixelMapping;

namespace ShowControl
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            LoggingBootstrap.Initialize();
            Host.Host.Main(new string[] { }, OnAudioFrameProcessed, HandlePixelMap).Wait();
        }

        private static void OnAudioFrameProcessed(object sender, AudioFrameProcessedEventArgs e)
        {
            Serilog.Log.Verbose("BPM {Bpm}", e.Bpm);
        }

        private static void HandlePixelMap(PixelMapSummary summary)
        {

        }
    }
}
