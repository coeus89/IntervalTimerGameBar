using System.Collections.Generic;
using System.IO;
using IntervalTimer.Core;
using Windows.Storage;

namespace IntervalTimerWidget
{
    /// <summary>
    /// Backs <see cref="TimerSettings"/> (and the run-state keys) with
    /// <c>settings.json</c> in the widget's local app data folder — the same place imported
    /// sounds already live. One-time: if the file is missing, values already stored in
    /// <see cref="ApplicationDataContainer.Values"/> are copied in so an upgrade keeps them.
    /// </summary>
    internal sealed class AppDataSettingsStore : ISettingsStore
    {
        private readonly string _path;
        private readonly Dictionary<string, object> _values;

        public AppDataSettingsStore()
        {
            _path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "settings.json");
            _values = new Dictionary<string, object>();

            if (!TryLoadFile())
                MigrateFromLocalSettings();
        }

        public bool TryGet(string key, out object value)
        {
            return _values.TryGetValue(key, out value);
        }

        public void Set(string key, object value)
        {
            _values[key] = value;
            Flush();
        }

        private bool TryLoadFile()
        {
            try
            {
                if (!File.Exists(_path)) return false;
                var parsed = FlatSettingsJson.Deserialize(File.ReadAllText(_path));
                foreach (var pair in parsed)
                    _values[pair.Key] = pair.Value;
                return true;
            }
            catch
            {
                // Missing or corrupt — fall through to the previous LocalSettings values.
                return false;
            }
        }

        private void MigrateFromLocalSettings()
        {
            try
            {
                var local = ApplicationData.Current.LocalSettings.Values;
                foreach (var key in local.Keys)
                {
                    var value = local[key];
                    if (value is int || value is long || value is double || value is bool || value is string)
                        _values[key] = value;
                }
            }
            catch
            {
                // LocalSettings unavailable — start empty.
            }
            Flush();
        }

        private void Flush()
        {
            try
            {
                var json = FlatSettingsJson.Serialize(_values);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, json);
                if (File.Exists(_path))
                    File.Replace(temp, _path, null);
                else
                    File.Move(temp, _path);
            }
            catch
            {
                // disk full / locked — keep the in-memory value
            }
        }
    }
}
