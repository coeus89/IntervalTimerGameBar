using System;
using System.Timers;

namespace IntervalTimer.Core
{
    /// <summary>
    /// A drift-free repeating interval timer. Instead of decrementing a counter every second
    /// (which accumulates <see cref="System.Windows.Threading"/> / <c>DispatcherTimer</c> drift),
    /// it stores an absolute UTC "next fire" instant and compares against the wall clock on each poll.
    ///
    /// Events are raised on a thread-pool thread. UI consumers must marshal to their own dispatcher.
    /// </summary>
    public sealed class IntervalTimerEngine : IDisposable
    {
        public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan MaxInterval = TimeSpan.FromHours(24);

        private const double PollMilliseconds = 250;

        private readonly Timer _ticker;
        private readonly object _gate = new object();

        private TimeSpan _interval = TimeSpan.FromSeconds(60);
        private DateTime _nextFireUtc;
        private bool _running;

        public IntervalTimerEngine()
        {
            _ticker = new Timer(PollMilliseconds) { AutoReset = true };
            _ticker.Elapsed += OnPoll;
        }

        /// <summary>Fires ~4x/second and on every state change; carries the remaining time for the UI.</summary>
        public event EventHandler<IntervalTickEventArgs> Tick;

        /// <summary>Fires once per interval boundary. The consumer plays the configured sound.</summary>
        public event EventHandler<IntervalElapsedEventArgs> Elapsed;

        public TimeSpan Interval
        {
            get { lock (_gate) { return _interval; } }
            set
            {
                var clamped = Clamp(value);
                lock (_gate)
                {
                    _interval = clamped;
                    if (_running)
                        _nextFireUtc = DateTime.UtcNow + clamped;
                }
                RaiseTick();
            }
        }

        /// <summary>Convenience for whole-second interval configuration from UI.</summary>
        public int IntervalSeconds
        {
            get { return (int)Math.Round(Interval.TotalSeconds); }
            set { Interval = TimeSpan.FromSeconds(value); }
        }

        public bool IsRunning
        {
            get { lock (_gate) { return _running; } }
        }

        public TimeSpan Remaining
        {
            get
            {
                lock (_gate)
                {
                    if (!_running) return _interval;
                    var remaining = _nextFireUtc - DateTime.UtcNow;
                    return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
                }
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_running) return;
                _running = true;
                _nextFireUtc = DateTime.UtcNow + _interval;
                _ticker.Start();
            }
            RaiseTick();
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (!_running) return;
                _running = false;
                _ticker.Stop();
            }
            RaiseTick();
        }

        public void Toggle()
        {
            if (IsRunning) Stop(); else Start();
        }

        /// <summary>Restart the countdown from a full interval without stopping.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                if (_running)
                    _nextFireUtc = DateTime.UtcNow + _interval;
            }
            RaiseTick();
        }

        /// <summary>
        /// Restore a schedule after a process was suspended/resumed (Game Bar widget) so the countdown
        /// picks up where it left off instead of restarting. Ignored if the instant is already in the past
        /// by more than one interval, in which case the caller should just <see cref="Start"/>.
        /// </summary>
        public void ResumeAt(DateTime nextFireUtc)
        {
            lock (_gate)
            {
                _running = true;
                _nextFireUtc = nextFireUtc;
                _ticker.Start();
            }
            RaiseTick();
        }

        /// <summary>Absolute instant the next <see cref="Elapsed"/> is due (UTC). Persist this across suspend.</summary>
        public DateTime NextFireUtc
        {
            get { lock (_gate) { return _nextFireUtc; } }
        }

        private void OnPoll(object sender, ElapsedEventArgs e)
        {
            var now = DateTime.UtcNow;
            int missed = 0;

            lock (_gate)
            {
                if (!_running) return;

                while (now >= _nextFireUtc)
                {
                    missed++;
                    _nextFireUtc += _interval;
                    // Guard against a runaway loop after a long sleep: realign to the wall clock.
                    if (missed > 1)
                    {
                        _nextFireUtc = now + _interval;
                        break;
                    }
                }
            }

            if (missed > 0)
            {
                var handler = Elapsed;
                if (handler != null)
                    handler(this, new IntervalElapsedEventArgs(now, missed > 1));
            }

            RaiseTick();
        }

        private void RaiseTick()
        {
            var handler = Tick;
            if (handler != null)
                handler(this, new IntervalTickEventArgs(Remaining, IsRunning));
        }

        private static TimeSpan Clamp(TimeSpan value)
        {
            if (value < MinInterval) return MinInterval;
            if (value > MaxInterval) return MaxInterval;
            return value;
        }

        public void Dispose()
        {
            _ticker.Elapsed -= OnPoll;
            _ticker.Dispose();
        }
    }
}
