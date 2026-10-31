namespace IntervalTimer.Core
{
    /// <summary>How the app announces each interval boundary.</summary>
    public enum AnnouncementMode
    {
        /// <summary>Play the configured sound only (the original, default behavior).</summary>
        Sound,

        /// <summary>Speak the configured phrase only.</summary>
        Speech,

        /// <summary>Play the sound, then speak the configured phrase.</summary>
        SoundThenSpeech,
    }
}
