using System;
using System.Globalization;
using System.Threading.Tasks;
using IntervalTimer.Core;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace IntervalTimerOverlay
{
    /// <summary>
    /// Plays the configured sound and/or speech for one interval boundary, in the order the user
    /// picked (<see cref="TimerSettings.AnnouncementMode"/>). Owns a second <see cref="MediaPlayer"/>
    /// dedicated to speech so sound and speech playback never fight over one player's <c>Source</c>.
    /// </summary>
    internal sealed class AlertPlayer : IDisposable
    {
        // How long to wait for the sound to finish before speaking anyway, in SoundThenSpeech mode.
        // A sound that didn't raise MediaEnded (device hiccup) shouldn't block the announcement.
        private static readonly TimeSpan SoundWaitTimeout = TimeSpan.FromSeconds(3);

        private readonly MediaSoundPlayer _sound;
        private readonly ISpeechProvider _speech;
        private readonly MediaPlayer _speechPlayer = new() { AudioCategory = MediaPlayerAudioCategory.Alerts };

        private string? _cacheKey;
        private SpeechAudio? _cachedAudio;

        public AlertPlayer(MediaSoundPlayer sound, ISpeechProvider speech)
        {
            _sound = sound;
            _speech = speech;
        }

        /// <param name="sound">The resolved sound to play — may be an import, which
        /// <see cref="TimerSettings.Sound"/> can't resolve on its own.</param>
        public async Task PlayAsync(TimerSettings settings, SoundInfo sound, AnnouncementContext context)
        {
            switch (settings.AnnouncementMode)
            {
                case AnnouncementMode.Sound:
                    _sound.Play(sound, settings.Volume);
                    break;

                case AnnouncementMode.Speech:
                    await SpeakAsync(settings, context);
                    break;

                case AnnouncementMode.SoundThenSpeech:
                    await _sound.PlayAndWaitAsync(sound, settings.Volume, SoundWaitTimeout);
                    await SpeakAsync(settings, context);
                    break;
            }
        }

        private async Task SpeakAsync(TimerSettings settings, AnnouncementContext context)
        {
            try
            {
                var text = AnnouncementFormatter.Format(settings.SpeechText, context);
                var audio = await GetAudioAsync(text, settings.VoiceId, settings.SpeechRate);

                _speechPlayer.Volume = Math.Clamp(settings.Volume, 0.0, 1.0);
                _speechPlayer.Source = MediaSource.CreateFromStream(audio.Stream, audio.ContentType);
                _speechPlayer.Play();
            }
            catch
            {
                // Speech is best-effort — a synthesis failure shouldn't take down the timer.
            }
        }

        private async Task<SpeechAudio> GetAudioAsync(string text, string voiceId, double rate)
        {
            var key = text + "\u0001" + voiceId + "\u0001" + rate.ToString(CultureInfo.InvariantCulture);
            if (_cachedAudio != null && _cacheKey == key)
            {
                _cachedAudio.Stream.Seek(0);
                return _cachedAudio;
            }

            var audio = await _speech.SynthesizeAsync(text, voiceId, rate);
            _cachedAudio?.Stream.Dispose();
            _cachedAudio = audio;
            _cacheKey = key;
            return audio;
        }

        public void Dispose()
        {
            _speechPlayer.Dispose();
            _cachedAudio?.Stream.Dispose();
        }
    }
}
