using System;
using System.Threading.Tasks;
using IntervalTimer.Core;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace IntervalTimerWidget
{
    /// <summary>Plays catalog sounds (built-in or imported) through a reusable <see cref="MediaPlayer"/>.</summary>
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
                _player.Volume = Clamp(volume);
                _player.Source = MediaSource.CreateFromUri(new Uri(sound.PlaybackUri));
                _player.PlaybackSession.Position = TimeSpan.Zero;
                _player.Play();
            }
            catch
            {
                // Don't let an audio-device hiccup take down the widget.
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
            TypedEventHandler<MediaPlayer, object> onEnded = (s, e) => tcs.TrySetResult(true);
            TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs> onFailed = (s, e) => tcs.TrySetResult(true);

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

        private static double Clamp(double volume) => Math.Min(Math.Max(volume, 0.0), 1.0);

        public void Dispose() => _player.Dispose();
    }
}
