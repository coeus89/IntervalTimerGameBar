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
        }
    }
}
