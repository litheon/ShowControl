using System;

namespace ShowControl.Utilities
{
    public enum OutputDeviceLogAction
    {
        /// <summary>Nothing worth logging — steady state, or a failure already reported.</summary>
        None,
        /// <summary>First failure after being healthy. Log once, with the exception.</summary>
        WentDown,
        /// <summary>Still failing; periodic reminder so a long outage is not silent.</summary>
        StillDown,
        /// <summary>First success after a failure. Log once.</summary>
        Recovered
    }

    public readonly record struct OutputDeviceTransition(
        OutputDeviceLogAction Action,
        int FailedAttempts,
        TimeSpan DownFor);

    /// <summary>
    /// Tracks send health for a single output device so a persistent failure
    /// (an unplugged or unreachable controller) neither floods the log nor is
    /// retried on every frame.
    ///
    /// Pure state machine — the caller supplies a monotonic clock reading, so this
    /// stays deterministic and testable, and costs nothing but a compare on the
    /// render loop's hot path.
    /// </summary>
    public sealed class OutputDeviceHealth
    {
        public static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan DefaultSummaryInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Consecutive successful sends required before an outage is declared over.
        /// A controller that flaps (a connected UDP socket alternating between sends
        /// and ICMP unreachable) would otherwise log a fresh outage on every cycle.
        /// </summary>
        public const int DefaultSuccessesToRecover = 3;

        private enum State
        {
            Healthy,
            /// <summary>Failing; sends are backed off to one per retry interval.</summary>
            Down,
            /// <summary>Sending again at full rate, but not yet declared recovered.</summary>
            Probation
        }

        private readonly TimeSpan _retryInterval;
        private readonly TimeSpan _summaryInterval;
        private readonly int _successesToRecover;

        private State _state = State.Healthy;
        private int _consecutiveFailures;
        private int _consecutiveSuccesses;
        private TimeSpan _downSince;
        private TimeSpan _lastAttempt;
        private TimeSpan _lastSummary;

        public OutputDeviceHealth()
            : this(DefaultRetryInterval, DefaultSummaryInterval, DefaultSuccessesToRecover)
        {
        }

        public OutputDeviceHealth(TimeSpan retryInterval, TimeSpan summaryInterval,
            int successesToRecover = DefaultSuccessesToRecover)
        {
            _retryInterval = retryInterval;
            _summaryInterval = summaryInterval;
            _successesToRecover = Math.Max(1, successesToRecover);
        }

        public bool IsDown => _state != State.Healthy;

        public int ConsecutiveFailures => _consecutiveFailures;

        /// <summary>
        /// True when the caller should attempt a send. Healthy devices are always
        /// attempted, and so are devices on probation — output resumes at full rate on
        /// the first success, so a recovered controller is never held dark waiting to
        /// prove itself. Only a device in backoff is skipped, costing one throw per
        /// retry interval instead of one per frame.
        /// </summary>
        public bool ShouldAttempt(TimeSpan now)
        {
            if (_state != State.Down)
            {
                return true;
            }
            return now - _lastAttempt >= _retryInterval;
        }

        public OutputDeviceTransition RecordSuccess(TimeSpan now)
        {
            _lastAttempt = now;

            if (_state == State.Healthy)
            {
                _consecutiveFailures = 0;
                return new OutputDeviceTransition(OutputDeviceLogAction.None, 0, TimeSpan.Zero);
            }

            _state = State.Probation;
            _consecutiveSuccesses++;

            if (_consecutiveSuccesses < _successesToRecover)
            {
                return new OutputDeviceTransition(
                    OutputDeviceLogAction.None, _consecutiveFailures, now - _downSince);
            }

            var transition = new OutputDeviceTransition(
                OutputDeviceLogAction.Recovered, _consecutiveFailures, now - _downSince);
            _state = State.Healthy;
            _consecutiveFailures = 0;
            _consecutiveSuccesses = 0;
            return transition;
        }

        public OutputDeviceTransition RecordFailure(TimeSpan now)
        {
            _lastAttempt = now;
            _consecutiveFailures++;
            _consecutiveSuccesses = 0;

            if (_state == State.Healthy)
            {
                _state = State.Down;
                _downSince = now;
                _lastSummary = now;
                return new OutputDeviceTransition(
                    OutputDeviceLogAction.WentDown, _consecutiveFailures, TimeSpan.Zero);
            }

            // Already reported, including a flap back out of probation: stay quiet
            // apart from the periodic reminder.
            _state = State.Down;

            if (now - _lastSummary >= _summaryInterval)
            {
                _lastSummary = now;
                return new OutputDeviceTransition(
                    OutputDeviceLogAction.StillDown, _consecutiveFailures, now - _downSince);
            }

            return new OutputDeviceTransition(
                OutputDeviceLogAction.None, _consecutiveFailures, now - _downSince);
        }
    }
}
