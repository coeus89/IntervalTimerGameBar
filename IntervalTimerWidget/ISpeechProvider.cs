using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace IntervalTimerWidget
{
    /// <summary>
    /// Converts text to an audio stream. Implemented today by <see cref="WindowsSpeechProvider"/>
    /// (the offline, built-in Windows voices). A cloud provider — e.g. Azure Speech, for
    /// higher-quality neural voices — could implement this same seam later without touching
    /// <see cref="AlertPlayer"/> or the UI.
    /// </summary>
    internal interface ISpeechProvider
    {
        /// <summary>Voices available right now. May be empty if the platform has none installed.</summary>
        IReadOnlyList<VoiceOption> Voices { get; }

        /// <param name="text">Literal text to speak (already expanded — no template tokens).</param>
        /// <param name="voiceId">A <see cref="VoiceOption.Id"/>, or empty/null for the platform default.</param>
        /// <param name="rate">Speaking rate multiplier, 0.5–2.0.</param>
        Task<SpeechAudio> SynthesizeAsync(string text, string voiceId, double rate);
    }

    /// <summary>
    /// A synthesized audio stream plus its MIME content type — <see cref="IRandomAccessStream"/> alone
    /// doesn't carry that, and <see cref="Windows.Media.Core.MediaSource.CreateFromStream"/> needs it.
    /// </summary>
    internal sealed class SpeechAudio
    {
        public SpeechAudio(IRandomAccessStream stream, string contentType)
        {
            Stream = stream;
            ContentType = contentType;
        }

        public IRandomAccessStream Stream { get; }
        public string ContentType { get; }
    }
}
