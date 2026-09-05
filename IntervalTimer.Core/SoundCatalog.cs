using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IntervalTimer.Core
{
    /// <summary>One selectable beep sound. <see cref="FileName"/> lives under <c>Assets/Sounds/</c> in each app.</summary>
    public sealed class SoundInfo
    {
        public SoundInfo(string key, string displayName, string fileName)
        {
            Key = key;
            DisplayName = displayName;
            FileName = fileName;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string FileName { get; }

        /// <summary>Packaged-app URI usable by MediaPlayer in both UWP and WinUI 3.</summary>
        public string AppxUri => "ms-appx:///Assets/Sounds/" + FileName;

        public override string ToString() => DisplayName;
    }

    public static class SoundCatalog
    {
        public static ReadOnlyCollection<SoundInfo> All { get; } = new ReadOnlyCollection<SoundInfo>(new List<SoundInfo>
        {
            new SoundInfo("beep",       "Beep",         "beep.wav"),
            new SoundInfo("doublebeep", "Double beep",  "doublebeep.wav"),
            new SoundInfo("chime",      "Chime",        "chime.wav"),
            new SoundInfo("bell",       "Bell",         "bell.wav"),
            new SoundInfo("alarm",      "Alarm",        "alarm.wav"),
        });

        public static SoundInfo Default => All[0];

        public static SoundInfo FromKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return Default;
            return All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Default;
        }
    }
}
