using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntervalTimer.Core;

namespace IntervalTimerOverlay
{
    /// <summary>
    /// Backs <see cref="TimerSettings"/> with a JSON file under %LOCALAPPDATA%. Works without
    /// package identity, so the app can run as a plain unpackaged .exe. Uses a source-generated
    /// serializer so the app stays trim/AOT-safe.
    /// </summary>
    internal sealed class JsonSettingsStore : ISettingsStore
    {
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
                    var parsed = JsonSerializer.Deserialize(
                        File.ReadAllText(_path), SettingsJsonContext.Default.DictionaryStringJsonElement);
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
            // ISettingsStore only ever stores int/double/bool/string - all registered on the context.
            _values[key] = JsonSerializer.SerializeToElement(
                value, value?.GetType() ?? typeof(string), SettingsJsonContext.Default);
            try
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(
                    _values, SettingsJsonContext.Default.DictionaryStringJsonElement));
            }
            catch
            {
                // disk full / locked - keep the in-memory value
            }
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(Dictionary<string, JsonElement>))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(string))]
    internal partial class SettingsJsonContext : JsonSerializerContext
    {
    }
}
