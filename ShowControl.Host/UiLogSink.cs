using System;
using System.Collections.Generic;
using System.Text;
using Serilog.Core;
using Serilog.Events;

namespace ShowControl.Host
{
    /// <summary>
    /// Keeps the most recent log events in memory so a UI can display them.
    /// Bounded ring buffer, safe to emit into from any thread. Consumers poll
    /// <see cref="ReadSince"/> on their own schedule rather than being called back
    /// per event, so a burst of logging can never drive UI work from a hot path.
    /// </summary>
    public sealed class UiLogSink : ILogEventSink
    {
        public readonly record struct LogLine(long Sequence, DateTimeOffset Timestamp, LogEventLevel Level, string Message);

        private const int DefaultCapacity = 1000;

        private readonly int _capacity;
        private readonly Queue<LogLine> _lines;
        private readonly object _sync = new object();
        private long _nextSequence;

        public static UiLogSink Instance { get; } = new UiLogSink(DefaultCapacity);

        public UiLogSink(int capacity)
        {
            _capacity = capacity;
            _lines = new Queue<LogLine>(capacity);
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null)
            {
                return;
            }

            var message = Render(logEvent);

            lock (_sync)
            {
                _lines.Enqueue(new LogLine(_nextSequence++, logEvent.Timestamp, logEvent.Level, message));
                while (_lines.Count > _capacity)
                {
                    _lines.Dequeue();
                }
            }
        }

        /// <summary>
        /// Returns buffered lines newer than <paramref name="afterSequence"/>, oldest first.
        /// Pass -1 to read everything currently buffered.
        /// </summary>
        public IReadOnlyList<LogLine> ReadSince(long afterSequence)
        {
            lock (_sync)
            {
                if (_lines.Count == 0)
                {
                    return Array.Empty<LogLine>();
                }

                var result = new List<LogLine>();
                foreach (var line in _lines)
                {
                    if (line.Sequence > afterSequence)
                    {
                        result.Add(line);
                    }
                }
                return result;
            }
        }

        private static string Render(LogEvent logEvent)
        {
            var builder = new StringBuilder();
            builder.Append(logEvent.RenderMessage());
            if (logEvent.Exception != null)
            {
                builder.Append(" | ")
                       .Append(logEvent.Exception.GetType().Name)
                       .Append(": ")
                       .Append(logEvent.Exception.Message);
            }
            return builder.ToString();
        }
    }
}
