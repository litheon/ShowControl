using System;

namespace ShowControl.Utilities
{
    /// <summary>
    /// Evaluates a time-of-day master-fader schedule. Waypoints are
    /// (hour as decimal, fader value); hours run from noon to noon, so an
    /// overnight show never crosses a wrap boundary: 8 PM is 20.0 and
    /// 4 AM the next morning is 28.0.
    /// </summary>
    public static class FaderSchedule
    {
        /// <summary>Converts a clock time to schedule hours (times before noon get +24).</summary>
        public static double ToScheduleHour(DateTime now)
        {
            double hour = now.Hour + now.Minute / 60.0 + now.Second / 3600.0;
            if (hour < 12.0) hour += 24.0; // e.g. 4 AM → 28.0
            return hour;
        }

        /// <summary>
        /// Linearly interpolates between adjacent waypoints; clamps to the first/last
        /// waypoint value outside the schedule's span. Waypoints must be sorted by hour.
        /// </summary>
        public static int Evaluate((double hour, int value)[] schedule, double scheduleHour)
        {
            if (schedule == null || schedule.Length == 0)
                throw new ArgumentException("Schedule must contain at least one waypoint.", nameof(schedule));

            if (scheduleHour <= schedule[0].hour)
                return schedule[0].value;

            if (scheduleHour >= schedule[schedule.Length - 1].hour)
                return schedule[schedule.Length - 1].value;

            for (int i = 0; i < schedule.Length - 1; i++)
            {
                var (h0, v0) = schedule[i];
                var (h1, v1) = schedule[i + 1];
                if (scheduleHour >= h0 && scheduleHour < h1)
                {
                    double t = (scheduleHour - h0) / (h1 - h0);
                    return (int)Math.Round(v0 + (v1 - v0) * t);
                }
            }

            return schedule[schedule.Length - 1].value;
        }
    }
}
