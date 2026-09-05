using System;
using IntervalTimer.Core;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace IntervalTimerWidget
{
    /// <summary>Plays catalog sounds through a reusable <see cref="MediaPlayer"/>.</summary>
    internal sealed class UwpSoundPlayer : ISoundPlayer, IDisposable
    {
        private readonly MediaPlayer _player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Alerts,
        };

        public void Play(SoundInfo sound, double volume)
        {
            if (sound == null) return;
            try
            {
                _player.Volume = Math.Min(Math.Max(volume, 0.0), 1.0);
                _player.Source = MediaSource.CreateFromUri(new Uri(sound.AppxUri));
                _player.PlaybackSession.Position = TimeSpan.Zero;
                _player.Play();
            }
            catch
            {
                // Don't let an audio-device hiccup take down the widget.
            }
        }

        public void Dispose() => _player.Dispose();
    }
}
