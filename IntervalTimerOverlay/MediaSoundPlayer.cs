using System;
using System.IO;
using IntervalTimer.Core;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace IntervalTimerOverlay
{
    /// <summary>Plays catalog sounds through a reusable <see cref="MediaPlayer"/>.</summary>
    internal sealed class MediaSoundPlayer : ISoundPlayer, IDisposable
    {
        private readonly MediaPlayer _player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Alerts };

        public void Play(SoundInfo sound, double volume)
        {
            if (sound == null) return;
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", sound.FileName);
                _player.Volume = Math.Min(Math.Max(volume, 0.0), 1.0);
                _player.Source = MediaSource.CreateFromUri(new Uri(path));
                _player.Position = TimeSpan.Zero;
                _player.Play();
            }
            catch
            {
                // A missing/locked audio device shouldn't crash the timer.
            }
        }

        public void Dispose() => _player.Dispose();
    }
}
