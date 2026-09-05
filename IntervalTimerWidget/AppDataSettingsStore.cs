using IntervalTimer.Core;
using Windows.Storage;

namespace IntervalTimerWidget
{
    /// <summary>Backs <see cref="TimerSettings"/> with the widget's local app-data settings.</summary>
    internal sealed class AppDataSettingsStore : ISettingsStore
    {
        private readonly ApplicationDataContainer _container = ApplicationData.Current.LocalSettings;

        public bool TryGet(string key, out object value)
        {
            return _container.Values.TryGetValue(key, out value);
        }

        public void Set(string key, object value)
        {
            _container.Values[key] = value;
        }
    }
}
