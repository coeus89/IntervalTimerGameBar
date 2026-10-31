using System;
using System.Globalization;
using System.Text;

namespace IntervalTimer.Core
{
    /// <summary>Values a spoken-phrase template can reference. See <see cref="AnnouncementFormatter"/>.</summary>
    public struct AnnouncementContext
    {
        public AnnouncementContext(TimeSpan interval, int count, DateTime localTime)
        {
            Interval = interval;
            Count = count;
            LocalTime = localTime;
        }

        /// <summary>The configured interval length (not the remaining time).</summary>
        public TimeSpan Interval { get; }

        /// <summary>How many times the interval has elapsed since the timer was last started.</summary>
        public int Count { get; }

        /// <summary>Wall-clock time of this announcement, for the <c>{time}</c> token.</summary>
        public DateTime LocalTime { get; }
    }

    /// <summary>
    /// Expands a user-authored phrase template (e.g. <c>"{interval} elapsed"</c>) into literal text
    /// for a speech provider. Pure string logic — no WinRT/platform dependency.
    /// </summary>
    public static class AnnouncementFormatter
    {
        /// <summary>The default template used when a user hasn't set one, or clears it.</summary>
        public const string DefaultTemplate = "{interval}";

        public static string Format(string template, AnnouncementContext context)
        {
            if (string.IsNullOrWhiteSpace(template)) template = DefaultTemplate;

            return template
                .Replace("{interval}", HumanizeInterval(context.Interval))
                .Replace("{count}", context.Count.ToString(CultureInfo.InvariantCulture))
                .Replace("{time}", context.LocalTime.ToString("h:mm tt", CultureInfo.InvariantCulture));
        }

        /// <summary>"1 minute 30 seconds", "2 hours", "45 seconds" — never "1 minutes" or "0 seconds" unless everything is zero.</summary>
        public static string HumanizeInterval(TimeSpan interval)
        {
            if (interval < TimeSpan.Zero) interval = TimeSpan.Zero;

            var hours = (int)interval.TotalHours;
            var minutes = interval.Minutes;
            var seconds = interval.Seconds;

            var sb = new StringBuilder();
            AppendUnit(sb, hours, "hour");
            AppendUnit(sb, minutes, "minute");
            // Always show seconds if nonzero, or as the sole "0 seconds" fallback when the whole
            // interval is zero (hours and minutes skip zero values to avoid "0 hours 0 minutes").
            if (seconds > 0 || sb.Length == 0)
                AppendUnit(sb, seconds, "second", forceShowZero: true);

            return sb.ToString();
        }

        private static void AppendUnit(StringBuilder sb, int value, string unit, bool forceShowZero = false)
        {
            if (value <= 0 && !(forceShowZero && value == 0)) return;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(value.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(unit);
            if (value != 1) sb.Append('s');
        }
    }
}
