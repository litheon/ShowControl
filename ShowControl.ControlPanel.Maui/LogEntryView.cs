using Microsoft.Maui.Graphics;
using Serilog.Events;
using ShowControl.Host;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// One row in the log pane. Immutable, so no change notification is needed.
    /// </summary>
    public sealed class LogEntryView
    {
        public LogEntryView(UiLogSink.LogLine line)
        {
            Sequence = line.Sequence;
            Timestamp = line.Timestamp.ToString("HH:mm:ss.fff");
            Level = Abbreviate(line.Level);
            LevelColor = ColorFor(line.Level);
            Message = line.Message;
        }

        public long Sequence { get; }
        public string Timestamp { get; }
        public string Level { get; }
        public Color LevelColor { get; }
        public string Message { get; }

        private static string Abbreviate(LogEventLevel level) => level switch
        {
            LogEventLevel.Verbose => "VRB",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => "???"
        };

        // Only the level tag is colored; the message keeps the theme's text color
        // so it stays readable in both light and dark.
        private static Color ColorFor(LogEventLevel level) => level switch
        {
            LogEventLevel.Verbose => Colors.Gray,
            LogEventLevel.Debug => Colors.Gray,
            LogEventLevel.Information => Colors.SteelBlue,
            LogEventLevel.Warning => Colors.Orange,
            LogEventLevel.Error => Colors.OrangeRed,
            LogEventLevel.Fatal => Colors.Red,
            _ => Colors.Gray
        };
    }
}
