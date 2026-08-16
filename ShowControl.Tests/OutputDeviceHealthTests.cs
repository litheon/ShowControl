using System;
using ShowControl.Utilities;
using Xunit;

namespace ShowControl.Tests
{
    public class OutputDeviceHealthTests
    {
        private static readonly TimeSpan Retry = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan Summary = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(21.739); // ~46 fps

        private static OutputDeviceHealth NewHealth(int successesToRecover = 1) =>
            new OutputDeviceHealth(Retry, Summary, successesToRecover);

        private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

        [Fact]
        public void HealthyDevice_IsAlwaysAttempted_AndLogsNothing()
        {
            var health = NewHealth();

            for (int frame = 0; frame < 100; frame++)
            {
                var now = Frame * frame;
                Assert.True(health.ShouldAttempt(now));
                Assert.Equal(OutputDeviceLogAction.None, health.RecordSuccess(now).Action);
            }

            Assert.False(health.IsDown);
        }

        [Fact]
        public void FirstFailure_LogsOnce_ThenSuppresses()
        {
            var health = NewHealth();

            Assert.Equal(OutputDeviceLogAction.WentDown, health.RecordFailure(At(0)).Action);
            Assert.True(health.IsDown);

            // Every subsequent failure inside the summary window stays silent.
            for (int i = 1; i < 50; i++)
            {
                Assert.Equal(OutputDeviceLogAction.None, health.RecordFailure(At(i * 0.1)).Action);
            }
        }

        [Fact]
        public void FailedDevice_IsOnlyRetriedOncePerRetryInterval()
        {
            var health = NewHealth();
            health.RecordFailure(At(0));

            // A whole second of frames at ~46fps produces one retry, not 46.
            int attempts = 0;
            for (int frame = 1; frame <= 46; frame++)
            {
                var now = Frame * frame;
                if (health.ShouldAttempt(now))
                {
                    attempts++;
                    health.RecordFailure(now);
                }
            }

            Assert.Equal(0, attempts); // 46 frames is just under the 1s retry window
            Assert.True(health.ShouldAttempt(At(1.0)));
        }

        [Fact]
        public void RetryWindow_ReopensAfterEachAttempt()
        {
            var health = NewHealth();
            health.RecordFailure(At(0));

            Assert.False(health.ShouldAttempt(At(0.99)));
            Assert.True(health.ShouldAttempt(At(1.0)));

            health.RecordFailure(At(1.0));
            Assert.False(health.ShouldAttempt(At(1.5)));
            Assert.True(health.ShouldAttempt(At(2.0)));
        }

        [Fact]
        public void StillDown_SummaryEmittedOncePerSummaryInterval()
        {
            var health = NewHealth();
            health.RecordFailure(At(0));

            Assert.Equal(OutputDeviceLogAction.None, health.RecordFailure(At(29)).Action);

            var summary = health.RecordFailure(At(30));
            Assert.Equal(OutputDeviceLogAction.StillDown, summary.Action);
            Assert.Equal(3, summary.FailedAttempts);
            Assert.Equal(At(30), summary.DownFor);

            Assert.Equal(OutputDeviceLogAction.None, health.RecordFailure(At(31)).Action);
            Assert.Equal(OutputDeviceLogAction.StillDown, health.RecordFailure(At(60)).Action);
        }

        [Fact]
        public void Recovery_LogsOnce_WithOutageDetail()
        {
            var health = NewHealth();
            health.RecordFailure(At(0));
            health.RecordFailure(At(1));
            health.RecordFailure(At(2));

            var recovered = health.RecordSuccess(At(5));

            Assert.Equal(OutputDeviceLogAction.Recovered, recovered.Action);
            Assert.Equal(3, recovered.FailedAttempts);
            Assert.Equal(At(5), recovered.DownFor);
            Assert.False(health.IsDown);
            Assert.Equal(0, health.ConsecutiveFailures);

            // Back to steady state: silent, and attempted every frame again.
            Assert.True(health.ShouldAttempt(At(5.001)));
            Assert.Equal(OutputDeviceLogAction.None, health.RecordSuccess(At(5.001)).Action);
        }

        [Fact]
        public void Recovery_RequiresSustainedSuccess()
        {
            var health = NewHealth(successesToRecover: 3);
            health.RecordFailure(At(0));

            Assert.Equal(OutputDeviceLogAction.None, health.RecordSuccess(At(1)).Action);
            Assert.Equal(OutputDeviceLogAction.None, health.RecordSuccess(At(2)).Action);
            Assert.Equal(OutputDeviceLogAction.Recovered, health.RecordSuccess(At(3)).Action);
            Assert.False(health.IsDown);
        }

        [Fact]
        public void Probation_SendsAtFullRate_SoOutputIsNotHeldDark()
        {
            // The first success must lift the backoff immediately; a recovered
            // controller should not sit dark while it proves itself.
            var health = NewHealth(successesToRecover: 3);
            health.RecordFailure(At(0));
            Assert.False(health.ShouldAttempt(At(0.5)));

            health.RecordSuccess(At(1));

            Assert.True(health.ShouldAttempt(At(1.001)));
            Assert.True(health.ShouldAttempt(At(1.02)));
        }

        [Fact]
        public void FlappingDevice_DoesNotLogEveryCycle()
        {
            // A connected UDP socket alternating between sends and ICMP unreachable
            // used to emit a fresh "went down" line on every cycle.
            var health = NewHealth(successesToRecover: 3);
            int logged = 0;

            for (int i = 0; i < 200; i++)
            {
                var now = At(i * 0.5);
                if (!health.ShouldAttempt(now))
                {
                    continue;
                }
                var transition = (i % 2 == 0) ? health.RecordFailure(now) : health.RecordSuccess(now);
                if (transition.Action != OutputDeviceLogAction.None)
                {
                    logged++;
                }
            }

            // One initial WentDown plus 30s summaries over the 100s run — never a
            // per-cycle line, and never a spurious Recovered.
            Assert.InRange(logged, 1, 5);
        }

        [Fact]
        public void SecondOutage_AfterRecovery_LogsAgain()
        {
            var health = NewHealth();
            health.RecordFailure(At(0));
            health.RecordSuccess(At(1));

            var second = health.RecordFailure(At(2));

            Assert.Equal(OutputDeviceLogAction.WentDown, second.Action);
            Assert.Equal(1, second.FailedAttempts); // counter reset by the recovery
        }

        [Fact]
        public void SustainedOutage_LogVolumeIsBounded()
        {
            // The scenario that started this: a controller down for an hour at ~46fps.
            var health = NewHealth();
            int logged = 0;
            int attempts = 0;

            for (long frame = 0; frame < 46L * 3600; frame++)
            {
                var now = Frame * frame;
                if (!health.ShouldAttempt(now))
                {
                    continue;
                }
                attempts++;
                if (health.RecordFailure(now).Action != OutputDeviceLogAction.None)
                {
                    logged++;
                }
            }

            // Retries land on frame boundaries, so each one falls just past the 1s
            // gate and the drift accumulates — slightly fewer than 3600 attempts and
            // 120 summaries over the hour. What matters is the order of magnitude.
            const long framesPerHour = 46L * 3600;
            Assert.InRange(attempts, 3500, 3600);   // ~1/s, not one per frame
            Assert.InRange(logged, 100, 130);       // ~2/min, not ~46/s
            Assert.True(logged < framesPerHour / 1000,
                $"expected log volume to drop by >1000x, got {logged} of {framesPerHour} frames");
        }
    }
}
