namespace IntervalTimer.Core
{
    /// <summary>
    /// Plays a short beep. Implemented per app because audio APIs are not part of netstandard2.0
    /// (UWP uses <c>Windows.Media.Playback.MediaPlayer</c>; the WinUI 3 overlay does likewise).
    /// </summary>
    public interface ISoundPlayer
    {
        /// <param name="sound">Which catalog sound to play.</param>
        /// <param name="volume">0.0 – 1.0.</param>
        void Play(SoundInfo sound, double volume);
    }
}
