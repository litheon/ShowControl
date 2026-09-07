using Microsoft.Extensions.Hosting;
using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
using ShowControl.Host;
using System;
using System.Runtime.InteropServices;
using ShowControl.Devices.Input;
using ShowControl.Graphics;
using ShowControl.PixelMapping;
using Microsoft.Extensions.Logging;

namespace ShowControl.ControlPanel.Maui
{
    public partial class MainPage : ContentPage
    {
        private readonly Task<IHost> _hostTask;
        private IHost _host;

        // Log pane: polls the in-memory sink rather than being called back per event,
        // so logging from the render/audio threads never touches the UI directly.
        private static readonly TimeSpan LogPollInterval = TimeSpan.FromMilliseconds(250);
        private readonly MainPageViewModel _viewModel = new MainPageViewModel();
        private IDispatcherTimer _logTimer;

        // Audio debug view: same poll-don't-push shape as the log pane — the analysis engine
        // publishes at ~94Hz on its own thread, and the timer pulls the latest snapshot at a
        // rate the UI can afford.
        private static readonly TimeSpan AudioDebugInterval = TimeSpan.FromMilliseconds(66);
        private readonly AudioDebugDrawable _audioDebugDrawable = new AudioDebugDrawable();
        private IDispatcherTimer _audioDebugTimer;
        private bool _loggedAudioDebugFailure;
        private bool _loggedLogPumpFailure;

        private static MainPage _instance;
        public MainPage()
        {
            if (_instance != null)
            {
                Serilog.Log.Warning("MainPage constructed twice; ignoring duplicate instance");
                return;
            }

            InitializeComponent();
            BindingContext = _viewModel;
            // Turns "the UI felt frozen" into logged durations, so main-thread stalls can be
            // correlated with whatever else the log says was happening at that moment.
            MainThreadWatchdog.Start();
            //Thread.Sleep(20000);
            _hostTask = Host.Host.Main(new string[] { }, OnAudioFrameProcessed, HandlePixelMap);
            Initialize();
            _instance = this;
        
            //Task.Run(() => new GraphicsHost().Run());
        }

        // Raised from the render thread when the map is published, so it marshals; the formatting
        // itself is the view-model's.
        private void HandlePixelMap(PixelMapSummary summary) =>
            MainThread.BeginInvokeOnMainThread(() => _viewModel.SetPixelMapSummary(summary));

        // The poll timers deliberately run for the life of the page, with NO pause mechanism.
        // Two were tried and both froze the log pane permanently on Catalyst multi-window:
        // Window Activated/Deactivated flaps (the PreviewPage polls for the same reason), and
        // page OnAppearing/OnDisappearing is wired to viewWillAppear/viewDidDisappear, which
        // the preview window's arrival triggers without any navigation — OnDisappearing fired,
        // OnAppearing never followed, and the timers died seconds after launch. Always-on is
        // affordable: the log is a plain text tail, so a tick is one string join and one text
        // layout, and the audio debug tick is a snapshot read and a small canvas invalidate.

        private void Initialize()
        {
            IHost result;
            try
            {
                result = _hostTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // This runs from the constructor, which UIKit calls inside viewDidLoad: an exception
                // escaping here aborts the process before there is a window to read the error in.
                // Come up without the host instead and put the failure on screen.
                Serilog.Log.Error(ex, "[MainPage] The host failed to start; the management UI is running without it.");
                _viewModel.ReportStartupFailure(ex);
                StartLogPane();
                return;
            }

            // Held so the preview window can resolve the pixel map editor, and registered so window
            // teardown and the platform lifecycle hooks have something to flush and stop.
            _host = result;
            AppLifetime.Register(result);
            Serilog.Log.Information("Doing platform initialization");
            result.Services.GetRequiredService<IInputService>().DoPlatformInitalization();

#if MACCATALYST || WINDOWS
            // Subscribe to the preview window requested event
            var graphicsHost = result.Services.GetRequiredService<IGraphicsHost>();
            graphicsHost.PreviewWindowRequested += OnPreviewWindowRequested;
#endif

            StartAudioDebugView(result);
            StartLogPane();
        }

        // The engine runs for the life of the app whatever is on screen; this view is just
        // one more consumer of it. Built here rather than in the constructor because the
        // engine lives in the host's services.
        private void StartAudioDebugView(IHost host)
        {
            var engine = host.Services.GetRequiredService<ShowControl.AudioAnalysis.AudioAnalysisEngine>();
            engine.Start();

            var audioViewModel = new ShowControl.ControlPanel.Editor.AudioDebugViewModel(
                engine,
                host.Services.GetService<ILogger<ShowControl.ControlPanel.Editor.AudioDebugViewModel>>(),
                host.Services.GetService<ShowControl.AudioAnalysis.SyntheticBeatMetadataSource>(),
                host.Services.GetService<ShowControl.AudioAnalysis.IBeatMetadataSource>());
            _viewModel.Audio = audioViewModel;

            _viewModel.AudioDevices = new ShowControl.ControlPanel.Editor.AudioDeviceListViewModel(
                new AudioDeviceSelector(host.Services.GetRequiredService<ShowControl.Devices.Audio.IAudioService>()),
                host.Services.GetService<ILogger<ShowControl.ControlPanel.Editor.AudioDeviceListViewModel>>());

            _audioDebugDrawable.SnapshotProvider = () => audioViewModel.Snapshot;
            AudioDebugView.Drawable = _audioDebugDrawable;

            _audioDebugTimer = Dispatcher.CreateTimer();
            _audioDebugTimer.Interval = AudioDebugInterval;
            _audioDebugTimer.Tick += (sender, e) =>
            {
                try
                {
                    audioViewModel.Refresh();
                    AudioDebugView.Invalidate();
                }
                catch (Exception ex)
                {
                    // A UI-callback exception aborts the process on Catalyst; log once, not per tick.
                    if (!_loggedAudioDebugFailure)
                    {
                        _loggedAudioDebugFailure = true;
                        Serilog.Log.Error(ex, "[MainPage] Audio debug tick failed; further failures suppressed.");
                    }
                }
            };
            _audioDebugTimer.Start();
        }

        private void StartLogPane()
        {
            PumpLogRows(); // show whatever was logged during startup

            _logTimer = Dispatcher.CreateTimer();
            _logTimer.Interval = LogPollInterval;
            _logTimer.Tick += (sender, e) =>
            {
                try
                {
                    PumpLogRows();
                }
                catch (Exception ex)
                {
                    // A UI-callback exception aborts the process on Catalyst; log once, not per tick.
                    if (!_loggedLogPumpFailure)
                    {
                        _loggedLogPumpFailure = true;
                        Serilog.Log.Error(ex, "[MainPage] Log pump tick failed; further failures suppressed.");
                    }
                }
            };
            _logTimer.Start();
        }

        // The log is one text block in a ScrollView, so following the tail is a scroll of
        // already-measured content — constant cost, unlike the CollectionView row churn (and
        // its end-of-list scrolls) whose O(row count) native work used to stall the main
        // thread whenever the pane was full and logs flowed.
        private void PumpLogRows()
        {
            if (_viewModel.PumpLogRows() && _viewModel.AutoScrollLog)
            {
                _ = LogScroll.ScrollToAsync(LogLabel, ScrollToPosition.End, animated: false);
            }
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

                // The graphics host keeps running when host startup failed, and asks for its window
                // anyway. Without the services behind it there is no map to edit, so say so rather
                // than opening a preview that cannot work.
                if (_host == null)
                {
                    Serilog.Log.Error("[MainPage] Preview window requested but the host did not start; not opening it.");
                    return;
                }

                Serilog.Log.Debug("[MainPage] Creating PreviewPage...");
                var pixelMapEditor = _host.Services.GetRequiredService<IPixelMapEditor>();

                // The page is built from the view-model, so the dialogs resolve it lazily rather
                // than being handed it: by the time a command can ask a question the page is up.
                PreviewPage? previewPage = null;
                var editorViewModel = new ShowControl.ControlPanel.Editor.PixelMapEditorViewModel(
                    pixelMapEditor,
                    _host.Services.GetRequiredService<IPixelTypeResolver>(),
                    _host.Services.GetService<ILogger<ShowControl.ControlPanel.Editor.PixelMapEditorViewModel>>(),
                    new PixelMapFileDialog(),
                    new MauiEditorDialogs(() => previewPage));
                previewPage = new PreviewPage(graphicsHost, editorViewModel, pixelMapEditor);

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

        // Host shutdown now runs from App.CreateWindow's Destroying handler via AppLifetime, which
        // is reached on a normal quit. The old MainWindow_Closing here was never subscribed to
        // anything, so StopAsync never ran.

        // The FFT polyline this used to drive is gone — the spectrum lives on the audio debug
        // drawable now, fed by the analysis engine. What remains of the legacy event is the
        // text readouts. Only scalars are copied before the marshal: the spectrum array is
        // pooled and owned by the event's dispatcher, so nothing here may retain it.
        private void OnAudioFrameProcessed(object sender, AudioFrameProcessedEventArgs e)
        {
            var bpm = e.Bpm;
            var averageLevel = e.AverageLevel;
            var currentLevel = e.CurrentLevel;
            var amplitudeRatio = e.AmplitudeRatio;
            MainThread.BeginInvokeOnMainThread(() =>
                _viewModel.UpdateAudioReadout(bpm, averageLevel, currentLevel, amplitudeRatio));
        }
    }
}
