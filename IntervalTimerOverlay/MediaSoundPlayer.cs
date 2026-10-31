using System;
using System.IO;
using System.Threading.Tasks;
using IntervalTimer.Core;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace IntervalTimerOverlay
{
    /// <summary>Plays catalog sounds (built-in or imported) through a reusable <see cref="MediaPlayer"/>.</summary>
    internal sealed class MediaSoundPlayer : ISoundPlayer, IDisposable
    {
        private readonly MediaPlayer _player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Alerts };

        public void Play(SoundInfo sound, double volume)
        {
            if (sound == null) return;
            try
            {
                // File paths rather than SoundInfo.PlaybackUri: ms-appx: / ms-appdata: need package identity.
                var path = sound.IsCustom
                    ? CustomSoundStore.PathFor(sound)
                    : Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", sound.FileName);
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

        /// <summary>
        /// Plays the sound and waits for it to finish (or <paramref name="timeout"/>), so a caller
        /// can chain a spoken announcement right after it instead of guessing a fixed delay.
        /// </summary>
        public async Task PlayAndWaitAsync(SoundInfo sound, double volume, TimeSpan timeout)
        {
            if (sound == null) return;

            var tcs = new TaskCompletionSource<bool>();
            TypedEventHandler<MediaPlayer, object> onEnded = (_, _) => tcs.TrySetResult(true);
            TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs> onFailed = (_, _) => tcs.TrySetResult(true);

            _player.MediaEnded += onEnded;
            _player.MediaFailed += onFailed;
            try
            {
                Play(sound, volume);
                await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            }
            finally
            {
                _player.MediaEnded -= onEnded;
                _player.MediaFailed -= onFailed;
            }
        }

        public void Dispose() => _player.Dispose();
    }
}
