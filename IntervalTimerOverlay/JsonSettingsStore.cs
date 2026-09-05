using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IntervalTimer.Core;

namespace IntervalTimerOverlay
{
    /// <summary>
    /// Backs <see cref="TimerSettings"/> with a JSON file under %LOCALAPPDATA%. Works without
    /// package identity, so the app can run as a plain unpackaged .exe.
    /// </summary>
    internal sealed class JsonSettingsStore : ISettingsStore
    {
        private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

        private readonly string _path;
        private readonly Dictionary<string, JsonElement> _values;

        public JsonSettingsStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IntervalTimerOverlay");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "settings.json");

            _values = new Dictionary<string, JsonElement>();
            try
            {
                if (File.Exists(_path))
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        File.ReadAllText(_path));
                    if (parsed != null) _values = parsed;
                }
            }
            catch
            {
                // Corrupt file - start fresh.
            }
        }

        public bool TryGet(string key, out object value)
        {
            if (_values.TryGetValue(key, out var el))
            {
                switch (el.ValueKind)
                {
                    case JsonValueKind.Number: value = el.GetDouble(); return true;
                    case JsonValueKind.True:
                    case JsonValueKind.False: value = el.GetBoolean(); return true;
                    case JsonValueKind.String: value = el.GetString() ?? string.Empty; return true;
                }
            }
            value = null!;
            return false;
        }

        public void Set(string key, object value)
        {
            _values[key] = JsonSerializer.SerializeToElement(value, Opts);
            try { File.WriteAllText(_path, JsonSerializer.Serialize(_values, Opts)); }
            catch { /* disk full / locked - keep the in-memory value */ }
        }
    }
}
