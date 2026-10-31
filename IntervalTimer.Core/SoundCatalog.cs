using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IntervalTimer.Core
{
    /// <summary>
    /// One selectable sound. Built-in sounds live under <c>Assets/Sounds/</c> in each app and are
    /// addressed with a plain key (e.g. <c>"chime"</c>). User-imported sounds are addressed with a
    /// <c>"custom:&lt;fileName&gt;"</c> key and live in the app's local storage — see
    /// <see cref="SoundCatalog.Custom"/> and <see cref="SoundCatalog.IsCustomKey"/>.
    /// </summary>
    public sealed class SoundInfo
    {
        public SoundInfo(string key, string displayName, string fileName, bool isCustom = false)
        {
            Key = key;
            DisplayName = displayName;
            FileName = fileName;
            IsCustom = isCustom;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string FileName { get; }

        /// <summary>True for a user-imported sound; false for one of the bundled tones.</summary>
        public bool IsCustom { get; }

        /// <summary>Packaged-app URI usable by MediaPlayer in both UWP and WinUI 3. Built-ins only.</summary>
        public string AppxUri => "ms-appx:///Assets/Sounds/" + FileName;

        /// <summary>
        /// The URI a player should actually open: the packaged asset for built-ins, or the app's
        /// local-storage "Sounds" folder for an import. Both schemes resolve via
        /// <c>MediaSource.CreateFromUri</c> in a packaged UWP/WinUI 3 app.
        /// </summary>
        public string PlaybackUri => IsCustom ? "ms-appdata:///local/Sounds/" + FileName : AppxUri;

        public override string ToString() => DisplayName;
    }

    public static class SoundCatalog
    {
        private const string CustomKeyPrefix = "custom:";

        public static ReadOnlyCollection<SoundInfo> All { get; } = new ReadOnlyCollection<SoundInfo>(new List<SoundInfo>
        {
            new SoundInfo("beep",       "Beep",         "beep.wav"),
            new SoundInfo("doublebeep", "Double beep",  "doublebeep.wav"),
            new SoundInfo("chime",      "Chime",        "chime.wav"),
            new SoundInfo("bell",       "Bell",         "bell.wav"),
            new SoundInfo("alarm",      "Alarm",        "alarm.wav"),
        });

        public static SoundInfo Default => All[0];

        /// <summary>Looks up a built-in sound by key. Unknown/blank/custom keys fall back to <see cref="Default"/>.</summary>
        public static SoundInfo FromKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return Default;
            return All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Default;
        }

        /// <summary>Builds the catalog entry for a user-imported file (file name only, no path).</summary>
        public static SoundInfo Custom(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentException("fileName is required", nameof(fileName));
            var display = System.IO.Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(display)) display = fileName;
            return new SoundInfo(CustomKeyPrefix + fileName, display, fileName, isCustom: true);
        }

        public static bool IsCustomKey(string key) =>
            key != null && key.StartsWith(CustomKeyPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Extracts the file name from a custom key, or null if <paramref name="key"/> isn't one.</summary>
        public static string CustomFileNameFromKey(string key) =>
            IsCustomKey(key) ? key.Substring(CustomKeyPrefix.Length) : null;

        /// <summary>
        /// Resolves a persisted <see cref="TimerSettings.SoundKey"/> against the built-in catalog and
        /// a caller-supplied list of currently-available custom sounds. Falls back to <see cref="Default"/>
        /// when a custom key no longer matches an existing file (e.g. it was deleted).
        /// </summary>
        public static SoundInfo Resolve(string key, IEnumerable<SoundInfo> customSounds)
        {
            if (IsCustomKey(key))
            {
                var match = customSounds?.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
                return match ?? Default;
            }
            return FromKey(key);
        }
    }
}
