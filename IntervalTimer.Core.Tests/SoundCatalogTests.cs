using IntervalTimer.Core;
using Xunit;

namespace IntervalTimer.Core.Tests
{
    public class SoundCatalogTests
    {
        [Fact]
        public void Catalog_has_the_five_bundled_tones()
        {
            Assert.Equal(5, SoundCatalog.All.Count);
            Assert.Equal(
                new[] { "beep", "doublebeep", "chime", "bell", "alarm" },
                System.Linq.Enumerable.Select(SoundCatalog.All, s => s.Key));
        }

        [Fact]
        public void Default_is_beep()
        {
            Assert.Equal("beep", SoundCatalog.Default.Key);
        }

        [Theory]
        [InlineData("chime")]
        [InlineData("CHIME")]
        [InlineData("Chime")]
        public void FromKey_is_case_insensitive(string key)
        {
            Assert.Equal("chime", SoundCatalog.FromKey(key).Key);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("nope")]
        public void FromKey_unknown_or_blank_returns_default(string? key)
        {
            Assert.Same(SoundCatalog.Default, SoundCatalog.FromKey(key!));
        }

        [Fact]
        public void AppxUri_and_ToString_are_well_formed()
        {
            var chime = SoundCatalog.FromKey("chime");
            Assert.Equal("ms-appx:///Assets/Sounds/chime.wav", chime.AppxUri);
            Assert.Equal("chime.wav", chime.FileName);
            Assert.Equal(chime.DisplayName, chime.ToString());
            Assert.False(chime.IsCustom);
            Assert.Equal(chime.AppxUri, chime.PlaybackUri);
        }

        [Fact]
        public void Custom_builds_a_custom_entry_keyed_by_file_name()
        {
            var sound = SoundCatalog.Custom("my-airhorn.mp3");

            Assert.True(sound.IsCustom);
            Assert.Equal("custom:my-airhorn.mp3", sound.Key);
            Assert.Equal("my-airhorn", sound.DisplayName);
            Assert.Equal("my-airhorn.mp3", sound.FileName);
            Assert.Equal("ms-appdata:///local/Sounds/my-airhorn.mp3", sound.PlaybackUri);
        }

        [Theory]
        [InlineData("custom:foo.wav", true)]
        [InlineData("CUSTOM:foo.wav", true)]
        [InlineData("chime", false)]
        [InlineData(null, false)]
        public void IsCustomKey_detects_the_prefix_case_insensitively(string key, bool expected)
        {
            Assert.Equal(expected, SoundCatalog.IsCustomKey(key));
        }

        [Fact]
        public void Resolve_returns_the_matching_custom_sound()
        {
            var custom = new[] { SoundCatalog.Custom("airhorn.wav") };
            var resolved = SoundCatalog.Resolve("custom:airhorn.wav", custom);
            Assert.Same(custom[0], resolved);
        }

        [Fact]
        public void Resolve_falls_back_to_default_when_the_custom_file_is_gone()
        {
            var resolved = SoundCatalog.Resolve("custom:deleted.wav", new SoundInfo[0]);
            Assert.Same(SoundCatalog.Default, resolved);
        }

        [Fact]
        public void Resolve_delegates_non_custom_keys_to_FromKey()
        {
            var resolved = SoundCatalog.Resolve("chime", null);
            Assert.Equal("chime", resolved.Key);
        }
    }
}
