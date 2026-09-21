using System.Collections.Generic;
using IntervalTimer.Core;

namespace IntervalTimer.Core.Tests
{
    /// <summary>In-memory <see cref="ISettingsStore"/> for tests.</summary>
    internal sealed class DictionarySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object> _map = new();

        public DictionarySettingsStore(params (string key, object value)[] seed)
        {
            foreach (var (k, val) in seed) _map[k] = val;
        }

        public bool TryGet(string key, out object value) => _map.TryGetValue(key, out value!);

        public void Set(string key, object value) => _map[key] = value;

        public int Count => _map.Count;
    }
}
