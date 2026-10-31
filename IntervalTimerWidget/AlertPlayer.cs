using System;
using System.Globalization;
using System.Threading.Tasks;
using IntervalTimer.Core;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace IntervalTimerWidget
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

        private readonly UwpSoundPlayer _sound;
        private readonly ISpeechProvider _speech;
        private readonly MediaPlayer _speechPlayer = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Alerts };

        private string _cacheKey;
        private SpeechAudio _cachedAudio;

        public AlertPlayer(UwpSoundPlayer sound, ISpeechProvider speech)
        {
            _sound = sound;
            _speech = speech;
        }

        public async Task PlayAsync(TimerSettings settings, AnnouncementContext context)
        {
            if (settings == null) return;

            switch (settings.AnnouncementMode)
            {
                case AnnouncementMode.Sound:
                    _sound.Play(settings.Sound, settings.Volume);
                    break;

                case AnnouncementMode.Speech:
                    await SpeakAsync(settings, context);
                    break;

                case AnnouncementMode.SoundThenSpeech:
                    await _sound.PlayAndWaitAsync(settings.Sound, settings.Volume, SoundWaitTimeout);
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

                _speechPlayer.Volume = Math.Min(Math.Max(settings.Volume, 0.0), 1.0);
                _speechPlayer.Source = MediaSource.CreateFromStream(audio.Stream, audio.ContentType);
                _speechPlayer.Play();
            }
            catch
            {
                // Speech is best-effort — a synthesis failure shouldn't take down the widget.
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
