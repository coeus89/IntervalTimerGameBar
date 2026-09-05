using System;

namespace IntervalTimer.Core
{
    /// <summary>
    /// Key/value persistence. Apps back this with <c>ApplicationData.Current.LocalSettings</c>;
    /// tests use an in-memory dictionary. Only primitive values (int/double/bool/string) are stored.
    /// </summary>
    public interface ISettingsStore
    {
        bool TryGet(string key, out object value);
        void Set(string key, object value);
    }

    /// <summary>User-configurable timer options, with load/save against an <see cref="ISettingsStore"/>.</summary>
    public sealed class TimerSettings
    {
        public const int DefaultIntervalSeconds = 60;
        public const double DefaultVolume = 0.8;

        public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;
        public string SoundKey { get; set; } = SoundCatalog.Default.Key;
        public double Volume { get; set; } = DefaultVolume;

        /// <summary>Whether the timer should auto-start when the app/widget loads.</summary>
        public bool AutoStart { get; set; }

        /// <summary>Game Bar widget: see-through background (show the game behind) vs a solid panel.</summary>
        public bool TransparentBackground { get; set; } = true;

        public SoundInfo Sound => SoundCatalog.FromKey(SoundKey);

        public TimeSpan Interval => TimeSpan.FromSeconds(
            Math.Min(Math.Max(IntervalSeconds, 1), (int)IntervalTimerEngine.MaxInterval.TotalSeconds));

        public static TimerSettings Load(ISettingsStore store)
        {
            var s = new TimerSettings();
            if (store == null) return s;

            object v;
            if (store.TryGet("IntervalSeconds", out v)) s.IntervalSeconds = ToInt(v, s.IntervalSeconds);
            if (store.TryGet("SoundKey", out v) && v is string) s.SoundKey = (string)v;
            if (store.TryGet("Volume", out v)) s.Volume = ToDouble(v, s.Volume);
            if (store.TryGet("AutoStart", out v)) s.AutoStart = ToBool(v, s.AutoStart);
            if (store.TryGet("TransparentBackground", out v)) s.TransparentBackground = ToBool(v, s.TransparentBackground);

            s.Normalize();
            return s;
        }

        public void Save(ISettingsStore store)
        {
            if (store == null) return;
            Normalize();
            store.Set("IntervalSeconds", IntervalSeconds);
            store.Set("SoundKey", SoundKey);
            store.Set("Volume", Volume);
            store.Set("AutoStart", AutoStart);
            store.Set("TransparentBackground", TransparentBackground);
        }

        public void Normalize()
        {
            if (IntervalSeconds < 1) IntervalSeconds = 1;
            var max = (int)IntervalTimerEngine.MaxInterval.TotalSeconds;
            if (IntervalSeconds > max) IntervalSeconds = max;
            if (Volume < 0) Volume = 0;
            if (Volume > 1) Volume = 1;
            SoundKey = SoundCatalog.FromKey(SoundKey).Key;
        }

        private static int ToInt(object v, int fallback)
        {
            try { return Convert.ToInt32(v); } catch { return fallback; }
        }

        private static double ToDouble(object v, double fallback)
        {
            try { return Convert.ToDouble(v); } catch { return fallback; }
        }

        private static bool ToBool(object v, bool fallback)
        {
            try { return Convert.ToBoolean(v); } catch { return fallback; }
        }
    }
}
