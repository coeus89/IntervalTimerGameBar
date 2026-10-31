using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace IntervalTimerWidget
{
    /// <summary>
    /// Speaks using the OneCore voices installed with Windows (Settings &gt; Time &amp; Language &gt;
    /// Speech &gt; Voices). Fully offline — no network access, no capability declaration needed.
    ///
    /// Narrator's newer "Natural" voices are not exposed through this public WinRT API; only the
    /// classic voices (David, Zira, Mark, plus any installed language packs) show up in
    /// <see cref="Voices"/>. A cloud provider behind <see cref="ISpeechProvider"/> would be the
    /// route to neural-quality voices in the future.
    /// </summary>
    internal sealed class WindowsSpeechProvider : ISpeechProvider, IDisposable
    {
        private readonly SpeechSynthesizer _synth = new SpeechSynthesizer();

        public IReadOnlyList<VoiceOption> Voices { get; } =
            SpeechSynthesizer.AllVoices
                .Select(v => new VoiceOption(v.Id, v.DisplayName + " (" + v.Language + ")"))
                .ToList();

        public async Task<SpeechAudio> SynthesizeAsync(string text, string voiceId, double rate)
        {
            if (string.IsNullOrWhiteSpace(text)) text = " ";

            _synth.Voice = ResolveVoice(voiceId);
            _synth.Options.SpeakingRate = Clamp(rate, 0.5, 2.0);

            var stream = await _synth.SynthesizeTextToStreamAsync(text);
            return new SpeechAudio(stream, stream.ContentType);
        }

        private static VoiceInformation ResolveVoice(string voiceId)
        {
            if (string.IsNullOrEmpty(voiceId)) return SpeechSynthesizer.DefaultVoice;
            return SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voiceId)
                   ?? SpeechSynthesizer.DefaultVoice;
        }

        private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        public void Dispose() => _synth.Dispose();
    }
}
