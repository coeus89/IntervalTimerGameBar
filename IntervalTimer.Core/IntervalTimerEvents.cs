using System;

namespace IntervalTimer.Core
{
    /// <summary>Raised roughly once per second (and on every state change) so a UI can redraw the countdown.</summary>
    public sealed class IntervalTickEventArgs : EventArgs
    {
        public IntervalTickEventArgs(TimeSpan remaining, bool isRunning)
        {
            Remaining = remaining;
            IsRunning = isRunning;
        }

        /// <summary>Time left until the next <see cref="IntervalTimerEngine.Elapsed"/>.</summary>
        public TimeSpan Remaining { get; }

        public bool IsRunning { get; }
    }

    /// <summary>Raised when an interval boundary is crossed. The consumer plays the beep.</summary>
    public sealed class IntervalElapsedEventArgs : EventArgs
    {
        public IntervalElapsedEventArgs(DateTime firedAtUtc, bool resynced)
        {
            FiredAtUtc = firedAtUtc;
            Resynced = resynced;
        }

        public DateTime FiredAtUtc { get; }

        /// <summary>
        /// True when more than one interval had elapsed since the last tick (e.g. the machine slept or
        /// a suspended widget was resumed). Only a single beep is raised and the schedule is realigned.
        /// </summary>
        public bool Resynced { get; }
    }
}
