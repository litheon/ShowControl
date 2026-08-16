using Microsoft.Extensions.Hosting;
using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
using ShowControl.Host;
using System;
using System.Collections.ObjectModel;
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

        // Log pane: polls the in-memory sink rather than being called back per event,
        // so logging from the render/audio threads never touches the UI directly.
        private const int MaxLogRowsShown = 500;
        private static readonly TimeSpan LogPollInterval = TimeSpan.FromMilliseconds(250);
        private readonly ObservableCollection<LogEntryView> _logRows = new ObservableCollection<LogEntryView>();
        private long _lastLogSequence = -1;
        private IDispatcherTimer _logTimer;

        private static MainPage _instance;
        public MainPage()
        {
            if (_instance != null)
            {
                Serilog.Log.Warning("MainPage constructed twice; ignoring duplicate instance");
                return;
            }

            InitializeComponent();
            _hostTask = Host.Host.Main(new string[] { }, OnAudioFrameProcessed, HandleDevices);
            Initialize();
            _instance = this;
            //Thread.Sleep(20000);
            //Task.Run(() => new GraphicsHost().Run());
        }

        // Summarizes the address map as one line per contiguous run of same-type
        // fixtures. Listing every device produced ~11.5k lines (~450 KB) for the
        // pixel surface, which is unreadable and slow to lay out in the label.
        private void HandleDevices(IDmxDevice[] devices)
        {
            int address = 1;
            var addressOutput = new StringBuilder();
            int runStartIndex = 0;
            int runStartAddress = address;

            for (int j = 0; j < devices.Length; j++)
            {
                address += devices[j].ChannelCount;

                bool lastOfRun = j == devices.Length - 1
                    || devices[j + 1].GetType() != devices[runStartIndex].GetType();

                if (lastOfRun)
                {
                    addressOutput.AppendLine(string.Format("Addresses {0}-{1}: {2} x {3}",
                        runStartAddress, address - 1, j - runStartIndex + 1, devices[runStartIndex].GetType().Name));
                    runStartIndex = j + 1;
                    runStartAddress = address;
                }
            }
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Addresses.Text = addressOutput.ToString();
            });
        }

        private void Initialize()
        {
            var result =  _hostTask.GetAwaiter().GetResult();
            Serilog.Log.Information("Doing platform initialization");
            result.Services.GetRequiredService<IInputService>().DoPlatformInitalization();

#if MACCATALYST || WINDOWS
            // Subscribe to the preview window requested event
            var graphicsHost = result.Services.GetRequiredService<IGraphicsHost>();
            graphicsHost.PreviewWindowRequested += OnPreviewWindowRequested;
#endif

            StartLogPane();
        }

        private void StartLogPane()
        {
            LogView.ItemsSource = _logRows;
            PumpLogRows(); // show whatever was logged during startup

            _logTimer = Dispatcher.CreateTimer();
            _logTimer.Interval = LogPollInterval;
            _logTimer.Tick += (sender, e) => PumpLogRows();
            _logTimer.Start();
        }

        private void PumpLogRows()
        {
            var newLines = UiLogSink.Instance.ReadSince(_lastLogSequence);
            if (newLines.Count == 0)
            {
                return;
            }

            foreach (var line in newLines)
            {
                _logRows.Add(new LogEntryView(line));
                _lastLogSequence = line.Sequence;
            }

            while (_logRows.Count > MaxLogRowsShown)
            {
                _logRows.RemoveAt(0);
            }

            if (LogAutoScroll.IsChecked && _logRows.Count > 0)
            {
                LogView.ScrollTo(_logRows.Count - 1, position: ScrollToPosition.End, animate: false);
            }
        }

        // Clears the on-screen rows only; the rolling log file is untouched.
        private void OnClearLogClicked(object sender, EventArgs e)
        {
            _logRows.Clear();
        }

#if MACCATALYST || WINDOWS
        private void OnPreviewWindowRequested(object? sender, ShowControl.Graphics.PreviewWindowRequestedEventArgs e)
        {
            try
            {
                Serilog.Log.Debug("[MainPage] Preview window requested: {Width}x{Height}", e.Width, e.Height);

                // Create a new MAUI window with the preview page
                var graphicsHost = sender as IGraphicsHost;
                if (graphicsHost == null)
                {
                    Serilog.Log.Error("[MainPage] graphicsHost is null");
                    return;
                }

                Serilog.Log.Debug("[MainPage] Creating PreviewPage...");
                var previewPage = new PreviewPage(graphicsHost);

                Serilog.Log.Debug("[MainPage] Creating Window...");
                var window = new Window(previewPage)
                {
                    Title = "Graphics Preview",
                };

                // Delay to allow main window to fully initialize (timing issue)
                Serilog.Log.Debug("[MainPage] Scheduling OpenWindow with delay...");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        Serilog.Log.Debug("[MainPage] Calling OpenWindow... Application.Current={ApplicationCurrent}", Application.Current);
                        if (Application.Current == null)
                        {
                            Serilog.Log.Error("[MainPage] Application.Current is null!");
                            return;
                        }
                        Application.Current.OpenWindow(window);
                        Serilog.Log.Debug("[MainPage] OpenWindow called successfully");
                    });
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[MainPage] Exception in OnPreviewWindowRequested");
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
