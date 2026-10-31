using System.Collections.Generic;
using IntervalTimer.Core;
using Xunit;

namespace IntervalTimer.Core.Tests
{
    public class FlatSettingsJsonTests
    {
        [Fact]
        public void Round_trip_preserves_settings_types_and_tick_precision()
        {
            var ticks = 638_000_123_456_789_123L;
            var original = new Dictionary<string, object>
            {
                ["IntervalSeconds"] = 45,
                ["Volume"] = 0.8,
                ["AutoStart"] = true,
                ["TransparentBackground"] = false,
                ["SoundKey"] = "custom:air horn.mp3",
                ["SpeechText"] = "line1\n\"quoted\"",
                ["NextFireUtcTicks"] = ticks,
            };

            var json = FlatSettingsJson.Serialize(original);
            var loaded = FlatSettingsJson.Deserialize(json);

            Assert.Equal(45L, loaded["IntervalSeconds"]);
            Assert.Equal(0.8, (double)loaded["Volume"], 5);
            Assert.Equal(true, loaded["AutoStart"]);
            Assert.Equal(false, loaded["TransparentBackground"]);
            Assert.Equal("custom:air horn.mp3", loaded["SoundKey"]);
            Assert.Equal("line1\n\"quoted\"", loaded["SpeechText"]);
            Assert.Equal(ticks, loaded["NextFireUtcTicks"]);
        }

        [Fact]
        public void Deserialize_empty_object()
        {
            var loaded = FlatSettingsJson.Deserialize("{ }");
            Assert.Empty(loaded);
        }

        [Fact]
        public void Deserialize_skips_null_values()
        {
            var loaded = FlatSettingsJson.Deserialize("{\"VoiceId\": null, \"Volume\": 1}");
            Assert.False(loaded.ContainsKey("VoiceId"));
            Assert.Equal(1L, loaded["Volume"]);
        }
    }
}
