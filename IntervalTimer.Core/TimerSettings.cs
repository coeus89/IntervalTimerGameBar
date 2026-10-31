using System;
using System.Globalization;

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
        public const double DefaultSpeechRate = 1.0;
        public const double MinSpeechRate = 0.5;
        public const double MaxSpeechRate = 2.0;

        public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;
        public string SoundKey { get; set; } = SoundCatalog.Default.Key;
        public double Volume { get; set; } = DefaultVolume;

        /// <summary>Whether the timer should auto-start when the app/widget loads.</summary>
        public bool AutoStart { get; set; }

        /// <summary>Game Bar widget: see-through background (show the game behind) vs a solid panel.</summary>
        public bool TransparentBackground { get; set; }

        /// <summary>Sound only (default), speech only, or sound-then-speech on each interval boundary.</summary>
        public AnnouncementMode AnnouncementMode { get; set; } = AnnouncementMode.Sound;

        /// <summary>Phrase template spoken when <see cref="AnnouncementMode"/> includes speech. See <see cref="AnnouncementFormatter"/>.</summary>
        public string SpeechText { get; set; } = AnnouncementFormatter.DefaultTemplate;

        /// <summary>Platform voice identifier. Empty/null means "use the platform default".</summary>
        public string VoiceId { get; set; } = string.Empty;

        /// <summary>Speaking rate multiplier, clamped to [<see cref="MinSpeechRate"/>, <see cref="MaxSpeechRate"/>].</summary>
        public double SpeechRate { get; set; } = DefaultSpeechRate;

        /// <summary>
        /// Resolves <see cref="SoundKey"/> against the built-in catalog only. A custom (imported)
        /// key resolves to <see cref="SoundCatalog.Default"/> here — callers that support custom
        /// sounds must resolve via <see cref="SoundCatalog.Resolve"/> with their current custom list.
        /// </summary>
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
            if (store.TryGet("AnnouncementMode", out v)) s.AnnouncementMode = ToMode(v as string, s.AnnouncementMode);
            if (store.TryGet("SpeechText", out v) && v is string) s.SpeechText = (string)v;
            if (store.TryGet("VoiceId", out v) && v is string) s.VoiceId = (string)v;
            if (store.TryGet("SpeechRate", out v)) s.SpeechRate = ToDouble(v, s.SpeechRate);

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
            store.Set("AnnouncementMode", AnnouncementMode.ToString());
            store.Set("SpeechText", SpeechText);
            store.Set("VoiceId", VoiceId ?? string.Empty);
            store.Set("SpeechRate", SpeechRate);
        }

        public void Normalize()
        {
            if (IntervalSeconds < 1) IntervalSeconds = 1;
            var max = (int)IntervalTimerEngine.MaxInterval.TotalSeconds;
            if (IntervalSeconds > max) IntervalSeconds = max;
            if (Volume < 0) Volume = 0;
            if (Volume > 1) Volume = 1;

            // A custom (imported) key can't be validated here — it depends on which files
            // currently exist, which only the app layer knows. Leave it as-is; SoundCatalog.Resolve
            // falls back to Default at playback/UI time if the file is gone. Only re-snap a
            // non-custom key to a known built-in.
            if (!SoundCatalog.IsCustomKey(SoundKey))
                SoundKey = SoundCatalog.FromKey(SoundKey).Key;

            if (string.IsNullOrWhiteSpace(SpeechText)) SpeechText = AnnouncementFormatter.DefaultTemplate;
            if (SpeechRate < MinSpeechRate) SpeechRate = MinSpeechRate;
            if (SpeechRate > MaxSpeechRate) SpeechRate = MaxSpeechRate;
            if (VoiceId == null) VoiceId = string.Empty;
        }

        // Values may arrive as a boxed primitive (ApplicationData) or a string (JSON, INI).
        // Always parse with the invariant culture so "0.8" never becomes 8 on a comma locale.
        private static int ToInt(object v, int fallback)
        {
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        private static double ToDouble(object v, double fallback)
        {
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        private static bool ToBool(object v, bool fallback)
        {
            if (v is string s) return bool.TryParse(s, out var b) ? b : fallback;
            try { return Convert.ToBoolean(v, CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        private static AnnouncementMode ToMode(string v, AnnouncementMode fallback)
        {
            if (string.IsNullOrEmpty(v)) return fallback;
            return Enum.TryParse(v, true, out AnnouncementMode m) ? m : fallback;
        }
    }
}
