using System;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShowControl.Devices.Audio;
using ShowControl.Host;
using ShowControl.PixelMapping;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// The management window's state: the audio readouts, the device summary and the log tail.
    /// <para>
    /// It lives in this project rather than <c>ShowControl.ControlPanel.Editor</c> because it reads
    /// the audio, host and pixel-mapping types, and pulling those into the plain-net9.0 editor
    /// project would drag MAUI in behind them and take the test project's reference with it. What it
    /// buys even here is that the callbacks off the audio and render threads assign properties
    /// instead of reaching into named controls.
    /// </para>
    /// <para>
    /// Every setter here has to run on the UI thread; the callers marshal.
    /// </para>
    /// </summary>
    public sealed partial class MainPageViewModel : ObservableObject
    {
        /// <summary>The log pane keeps the tail, not the session. The rolling file has the rest.</summary>
        private const int MaxLogRowsShown = 500;

        private long _lastLogSequence = -1;

        [ObservableProperty] private string _bpmText = "BPM";
        [ObservableProperty] private string _amplitudeRatioText = "Amplitude Ratio";
        [ObservableProperty] private string _bassAmplitudeCurrentText = "Current Bass Amplitude";
        [ObservableProperty] private string _bassAmplitudeAverageText = "Average Bass Amplitude";
        [ObservableProperty] private string _deviceSummaryText = "Devices";

        /// <summary>Whether the pane follows the tail. Bound to the checkbox; read by the page's timer.</summary>
        [ObservableProperty] private bool _autoScrollLog = true;

        /// <summary>The audio debug view's state. Set by the page once the host (and with it the
        /// analysis engine) is up; the debug panel's bindings sit dormant until then.</summary>
        [ObservableProperty] private ShowControl.ControlPanel.Editor.AudioDebugViewModel? _audio;

        /// <summary>The capture-device picker. Set alongside <see cref="Audio"/>.</summary>
        [ObservableProperty] private ShowControl.ControlPanel.Editor.AudioDeviceListViewModel? _audioDevices;

        /// <summary>The formatted lines behind <see cref="LogText"/>; a plain list, so trimming
        /// costs nothing on the UI beyond the single text reassignment.</summary>
        private readonly System.Collections.Generic.List<string> _logLines = new();

        /// <summary>The log tail as one text block. Rebuilt (a single property change and one
        /// native text layout) whenever new lines arrive — deliberately not a row collection,
        /// whose per-row native churn stalled the main thread once the pane filled.</summary>
        [ObservableProperty]
        private string _logText = string.Empty;

        // Scalars rather than the event args: the args own a pooled spectrum array and are
        // disposed by their dispatcher, so nothing that marshals may hold them.
        public void UpdateAudioReadout(float bpm, double averageLevel, double currentLevel, double amplitudeRatio)
        {
            BpmText = string.Format("BPM: {0}", bpm);
            BassAmplitudeAverageText = string.Format("Bass Amplitude Average: {0}", averageLevel);
            BassAmplitudeCurrentText = string.Format("Bass Amplitude Current: {0}", currentLevel);
            AmplitudeRatioText = string.Format("Amplitude Ratio: {0}", amplitudeRatio);
        }

        /// <summary>
        /// One line per fixture. This used to summarize an <c>IDmxDevice[]</c> that held one device
        /// per pixel — ~11.5k lines, ~450 KB — because the pixel surface had no notion of a fixture
        /// to group by.
        /// </summary>
        public void SetPixelMapSummary(PixelMapSummary summary)
        {
            var text = new StringBuilder();
            text.AppendLine(string.Format("{0} pixels · {1} channels · {2} output device(s)",
                summary.TotalPixelCount, summary.TotalChannelCount, summary.OutputCount));
            foreach (var line in summary.Lines)
            {
                text.AppendLine(line.ToString());
            }

            DeviceSummaryText = text.ToString();
        }

        /// <summary>Puts a startup failure where the device list would have been.</summary>
        public void ReportStartupFailure(Exception exception) =>
            DeviceSummaryText = "Startup failed: " + exception.Message;

        /// <summary>
        /// Appends whatever the in-memory sink has gathered since the last call. Returns true if
        /// anything arrived, which is the page's cue to scroll — scrolling is a control operation
        /// with no bindable equivalent, so it stays in the view.
        /// </summary>
        public bool PumpLogRows()
        {
            var newLines = UiLogSink.Instance.ReadSince(_lastLogSequence);
            if (newLines.Count == 0)
            {
                return false;
            }
            _lastLogSequence = newLines[newLines.Count - 1].Sequence;

            foreach (var line in newLines)
            {
                _logLines.Add(FormatLine(line));
            }
            if (_logLines.Count > MaxLogRowsShown)
            {
                _logLines.RemoveRange(0, _logLines.Count - MaxLogRowsShown);
            }
            LogText = string.Join('\n', _logLines);
            return true;
        }

        private static string FormatLine(UiLogSink.LogLine line)
        {
            var level = line.Level switch
            {
                Serilog.Events.LogEventLevel.Verbose => "VRB",
                Serilog.Events.LogEventLevel.Debug => "DBG",
                Serilog.Events.LogEventLevel.Information => "INF",
                Serilog.Events.LogEventLevel.Warning => "WRN",
                Serilog.Events.LogEventLevel.Error => "ERR",
                Serilog.Events.LogEventLevel.Fatal => "FTL",
                _ => "???",
            };
            return $"{line.Timestamp:HH:mm:ss.fff} {level} {line.Message}";
        }

        /// <summary>Clears the on-screen rows only; the rolling log file is untouched.</summary>
        [RelayCommand]
        private void ClearLog()
        {
            _logLines.Clear();
            LogText = string.Empty;
        }
    }
}
