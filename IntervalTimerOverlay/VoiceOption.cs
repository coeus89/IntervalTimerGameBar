namespace IntervalTimerOverlay
{
    /// <summary>One selectable text-to-speech voice, shown in the UI via <see cref="ToString"/>.</summary>
    internal sealed class VoiceOption
    {
        public VoiceOption(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }
}
