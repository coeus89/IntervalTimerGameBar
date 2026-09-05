using System;
using System.Threading;
using IntervalTimer.Core;
using Xunit;

namespace IntervalTimer.Core.Tests
{
    public class IntervalTimerEngineTests
    {
        [Fact]
        public void Interval_is_clamped_to_the_supported_range()
        {
            using var engine = new IntervalTimerEngine();

            engine.Interval = TimeSpan.Zero;
            Assert.Equal(IntervalTimerEngine.MinInterval, engine.Interval);

            engine.Interval = TimeSpan.FromDays(7);
            Assert.Equal(IntervalTimerEngine.MaxInterval, engine.Interval);
        }

        [Fact]
        public void Remaining_reports_the_full_interval_while_stopped()
        {
            using var engine = new IntervalTimerEngine { IntervalSeconds = 42 };
            Assert.False(engine.IsRunning);
            Assert.Equal(42, (int)Math.Round(engine.Remaining.TotalSeconds));
        }

        [Fact]
        public void Elapsed_fires_once_per_interval_without_drift()
        {
            using var engine = new IntervalTimerEngine { Interval = TimeSpan.FromSeconds(1) };
            int count = 0;
            engine.Elapsed += (_, _) => Interlocked.Increment(ref count);

            engine.Start();
            Thread.Sleep(4600); // expect boundaries at ~1s, 2s, 3s, 4s
            engine.Stop();

            Assert.InRange(count, 3, 5);
        }

        [Fact]
        public void Long_gap_produces_a_single_resynced_beep()
        {
            using var engine = new IntervalTimerEngine { Interval = TimeSpan.FromSeconds(1) };
            int count = 0;
            bool anyResynced = false;
            engine.Elapsed += (_, e) =>
            {
                Interlocked.Increment(ref count);
                if (e.Resynced) anyResynced = true;
            };

            engine.Start();
            Thread.Sleep(50);
            // Simulate a resume far in the past: the engine should fire once and realign.
            engine.ResumeAt(DateTime.UtcNow - TimeSpan.FromMinutes(5));
            Thread.Sleep(1500);
            engine.Stop();

            Assert.True(anyResynced);
            Assert.InRange(count, 1, 3);
        }
    }
}
