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

        private static MainPage _instance;
        public MainPage()
        {
            if (_instance != null)
            {
                return;
            }

            InitializeComponent();
            _hostTask = Host.Host.Main(new string[] { }, OnAudioFrameProcessed, HandleDevices);
            Initialize();
            _instance = this;
            //Thread.Sleep(20000);
            //Task.Run(() => new GraphicsHost().Run());
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

#if MACCATALYST
            // Subscribe to the preview window requested event
            var graphicsHost = result.Services.GetRequiredService<IGraphicsHost>();
            graphicsHost.PreviewWindowRequested += OnPreviewWindowRequested;
#endif
        }

#if MACCATALYST
        private void OnPreviewWindowRequested(object? sender, ShowControl.Graphics.PreviewWindowRequestedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] Preview window requested: {e.Width}x{e.Height}");

                // Create a new MAUI window with the preview page
                var graphicsHost = sender as IGraphicsHost;
                if (graphicsHost == null)
                {
                    System.Diagnostics.Debug.WriteLine("[MainPage] ERROR: graphicsHost is null");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("[MainPage] Creating PreviewPage...");
                var previewPage = new PreviewPage(graphicsHost);

                System.Diagnostics.Debug.WriteLine("[MainPage] Creating Window...");
                var window = new Window(previewPage)
                {
                    Title = "Graphics Preview",
                    Width = e.Width,
                    Height = e.Height
                };

                // Delay to allow main window to fully initialize (timing issue)
                System.Diagnostics.Debug.WriteLine("[MainPage] Scheduling OpenWindow with delay...");
                _ = Task.Run(async () =>
                {
                    //await Task.Delay(500);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainPage] Calling OpenWindow... Application.Current={Application.Current}");
                        Application.Current?.OpenWindow(window);
                        System.Diagnostics.Debug.WriteLine("[MainPage] OpenWindow called successfully");
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] EXCEPTION in OnPreviewWindowRequested: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MainPage] Stack trace: {ex.StackTrace}");
            }
        }
#endif

        private async void MainWindow_Closing(object sender, EventArgs e)
        {
            var result = await _hostTask;
            await result.StopAsync();
            result.Dispose();
        }

        private void OnAudioFrameProcessed(object sender, AudioFrameProcessedEventArgs e)
        {
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
                    var Y = fftChartPoints[i].Y > double.NegativeInfinity ? (fftChartPoints[i].Y / -140.0d) * polyline.Height : 0;

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
