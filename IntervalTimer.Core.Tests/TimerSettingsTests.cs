using System.Globalization;
using IntervalTimer.Core;
using Xunit;

namespace IntervalTimer.Core.Tests
{
    public class TimerSettingsTests
    {
        [Fact]
        public void Load_from_empty_store_returns_defaults()
        {
            var s = TimerSettings.Load(new DictionarySettingsStore());

            Assert.Equal(TimerSettings.DefaultIntervalSeconds, s.IntervalSeconds);
            Assert.Equal(SoundCatalog.Default.Key, s.SoundKey);
            Assert.Equal(TimerSettings.DefaultVolume, s.Volume);
            Assert.False(s.AutoStart);
            Assert.True(s.TransparentBackground);
        }

        [Fact]
        public void Save_then_Load_round_trips_every_field()
        {
            var store = new DictionarySettingsStore();
            var original = new TimerSettings
            {
                IntervalSeconds = 45,
                SoundKey = "chime",
                Volume = 0.33,
                AutoStart = true,
                TransparentBackground = false,
            };

            original.Save(store);
            var loaded = TimerSettings.Load(store);

            Assert.Equal(45, loaded.IntervalSeconds);
            Assert.Equal("chime", loaded.SoundKey);
            Assert.Equal(0.33, loaded.Volume, 3);
            Assert.True(loaded.AutoStart);
            Assert.False(loaded.TransparentBackground);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-5, 1)]
        [InlineData(999_999, 86_400)]
        [InlineData(120, 120)]
        public void Normalize_clamps_interval(int input, int expected)
        {
            var s = new TimerSettings { IntervalSeconds = input };
            s.Normalize();
            Assert.Equal(expected, s.IntervalSeconds);
        }

        [Theory]
        [InlineData(-0.5, 0.0)]
        [InlineData(1.5, 1.0)]
        [InlineData(0.5, 0.5)]
        public void Normalize_clamps_volume(double input, double expected)
        {
            var s = new TimerSettings { Volume = input };
            s.Normalize();
            Assert.Equal(expected, s.Volume);
        }

        [Fact]
        public void Unknown_sound_key_falls_back_to_default()
        {
            var s = TimerSettings.Load(new DictionarySettingsStore(("SoundKey", "does-not-exist")));
            Assert.Equal(SoundCatalog.Default.Key, s.SoundKey);
        }

        [Fact]
        public void Corrupt_values_fall_back_to_defaults()
        {
            var s = TimerSettings.Load(new DictionarySettingsStore(
                ("IntervalSeconds", "not-a-number"),
                ("Volume", new object()),
                ("AutoStart", "maybe")));

            Assert.Equal(TimerSettings.DefaultIntervalSeconds, s.IntervalSeconds);
            Assert.Equal(TimerSettings.DefaultVolume, s.Volume);
            Assert.False(s.AutoStart);
        }

        [Fact]
        public void String_values_parse_with_invariant_culture()
        {
            // A comma-locale machine must still read "0.8" as 0.8, not 8.
            var prev = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var s = TimerSettings.Load(new DictionarySettingsStore(
                    ("Volume", "0.8"), ("IntervalSeconds", "90"), ("AutoStart", "true")));

                Assert.Equal(0.8, s.Volume, 3);
                Assert.Equal(90, s.IntervalSeconds);
                Assert.True(s.AutoStart);
            }
            finally
            {
                CultureInfo.CurrentCulture = prev;
            }
        }

        [Fact]
        public void Interval_property_is_clamped_independently_of_Normalize()
        {
            var s = new TimerSettings { IntervalSeconds = 5_000_000 };
            Assert.Equal(IntervalTimerEngine.MaxInterval, s.Interval);
        }
    }
}
