using System;
using IntervalTimer.Core;
using Xunit;

namespace IntervalTimer.Core.Tests
{
    public class AnnouncementFormatterTests
    {
        [Theory]
        [InlineData(0, 0, 0, "0 seconds")]
        [InlineData(0, 0, 1, "1 second")]
        [InlineData(0, 0, 45, "45 seconds")]
        [InlineData(0, 1, 0, "1 minute")]
        [InlineData(0, 1, 30, "1 minute 30 seconds")]
        [InlineData(0, 2, 0, "2 minutes")]
        [InlineData(1, 0, 0, "1 hour")]
        [InlineData(2, 15, 0, "2 hours 15 minutes")]
        public void HumanizeInterval_formats_as_expected(int hours, int minutes, int seconds, string expected)
        {
            var interval = new TimeSpan(hours, minutes, seconds);
            Assert.Equal(expected, AnnouncementFormatter.HumanizeInterval(interval));
        }

        [Fact]
        public void HumanizeInterval_clamps_negative_to_zero_seconds()
        {
            Assert.Equal("0 seconds", AnnouncementFormatter.HumanizeInterval(TimeSpan.FromSeconds(-5)));
        }

        [Fact]
        public void Format_expands_all_tokens()
        {
            var context = new AnnouncementContext(
                TimeSpan.FromSeconds(90), count: 3, localTime: new DateTime(2026, 1, 1, 14, 5, 0));

            var text = AnnouncementFormatter.Format("#{count} — {interval} — {time}", context);

            Assert.Equal("#3 — 1 minute 30 seconds — 2:05 PM", text);
        }

        [Fact]
        public void Format_falls_back_to_default_template_when_blank()
        {
            var context = new AnnouncementContext(TimeSpan.FromSeconds(60), 1, DateTime.Now);
            var text = AnnouncementFormatter.Format("   ", context);
            Assert.Equal("1 minute", text);
        }

        [Fact]
        public void Format_leaves_unknown_tokens_untouched()
        {
            var context = new AnnouncementContext(TimeSpan.FromSeconds(10), 1, DateTime.Now);
            var text = AnnouncementFormatter.Format("hi {nope}", context);
            Assert.Equal("hi {nope}", text);
        }
    }
}
