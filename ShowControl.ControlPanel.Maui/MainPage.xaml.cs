using Microsoft.Extensions.Hosting;
using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
using System;
using System.Runtime.InteropServices;
using System.Text;
using Point = ShowControl.Devices.Audio.Point;
using GraphicsPoint = Microsoft.Maui.Graphics.Point;
using ShowControl.Devices.Input;
using ShowControl.Graphics;

namespace ShowControl.ControlPanel.Maui
{
    public partial class MainPage : ContentPage
    {
        private static readonly double Limit = 1;
        Point[] fftChartPoints;
        PointCollection points1;
        PointCollection points2;
        PointCollection activePoints;
        private readonly Task<IHost> _hostTask;
        int x = 10;

        public MainPage()
        {
            InitializeComponent();
            _hostTask = Host.Host.Main(new string[] { }, OnAudioFrameProcessed, HandleDevices);
            Initialize();
            //Thread.Sleep(20000);
            Task.Run(() => new GraphicsHost().Run());
        }

        private void HandleDevices(IDmxDevice[] devices)
        {
            int address = 1;
            var addressOutput = new StringBuilder();
            for (int j = 0; j < devices.Length; j++)
            {
                addressOutput.AppendLine(string.Format("Address {0} for {1}: {2}", j + 1, devices[j].GetType().ToString(), address));
                address += devices[j].ChannelCount;
            }
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Addresses.Text = addressOutput.ToString();
            });
        }

        private async void Initialize()
        {
            var result = await _hostTask;
            result.Services.GetRequiredService<IInputService>().DoPlatformInitalization();
        }

        private async void MainWindow_Closing(object sender, EventArgs e)
        {
            var result = await _hostTask;
            await result.StopAsync();
            result.Dispose();
        }

        private void OnAudioFrameProcessed(object sender, AudioFrameProcessedEventArgs e)
        {

            return;

            if (fftChartPoints == null || fftChartPoints.Length != e.AudioSpectrum.Points.Length)
            {
                Array.Resize(ref fftChartPoints, e.AudioSpectrum.Points.Length);
            }

            Array.Copy(e.AudioSpectrum.Points, fftChartPoints, fftChartPoints.Length);

            MainThread.BeginInvokeOnMainThread(() => {
                Bpm.Text = string.Format("BPM: {0}", e.Bpm);
                BassAmplitudeAverage.Text = string.Format("Bass Amplitude Average: {0}", e.AverageLevel);
                BassAmplitudeCurrent.Text = string.Format("Bass Amplitude Current: {0}", e.CurrentLevel);
                AmplitudeRatio.Text = string.Format("Amplitude Ratio: {0}", e.AmplitudeRatio);
                double previous = 0;
                GraphicsPoint point1 = default;
                GraphicsPoint point2 = default;

                if (points1 == null)
                {
                    points1 = new PointCollection();
                    points2 = new PointCollection();
                    activePoints = points1;
                }

                if (polyline.Points == points1)
                {
                    activePoints = points2;
                }
                else if (activePoints == points2)
                {
                    activePoints = points1;
                }

                activePoints.Clear();
                for (int i = 0; i < fftChartPoints.Length; i++)
                {
                    var X = fftChartPoints[i].X * polyline.Width;
                    var Y = (fftChartPoints[i].Y / -140.0d) * polyline.Height;

                    if (fftChartPoints[i].X <= Limit)
                    {
                        point1.X = previous;
                        point1.Y = Y;

                        point2.X = X;
                        point2.Y = Y;

                        if (activePoints.Count < fftChartPoints.Length * 2 + 1)
                        {
                            activePoints.Add(point1);
                            activePoints.Add(point2);
                        }
                        else
                        {
                            activePoints[2 * i] = point1;
                            activePoints[2 * i + 1] = point2;
                        }

                        previous = X;
                    }
                    else if (fftChartPoints[i].X > Limit)
                    {
                        break;
                    }
                }
                polyline.Points = activePoints;
            });
        }
    }
}
