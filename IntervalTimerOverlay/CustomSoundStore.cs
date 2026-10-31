using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntervalTimer.Core;
using Windows.Media.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace IntervalTimerOverlay
{
    /// <summary>
    /// Imports one user-supplied audio file into <c>%LOCALAPPDATA%\IntervalTimerOverlay\Sounds</c>
    /// (plain System.IO — the app is unpackaged, so there's no <c>ApplicationData</c>). That folder
    /// holds at most one file; importing another deletes the previous one. The bundled defaults
    /// next to the exe (<c>Assets\Sounds</c>) are never touched and always stay in the picker.
    /// </summary>
    internal sealed class CustomSoundStore
    {
        private const long MaxFileBytes = 50L * 1024 * 1024;
        private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(5);

        // Formats Windows.Media.Playback.MediaPlayer decodes natively, no extra codec needed.
        private static readonly string[] SupportedExtensions =
            { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac" };

        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntervalTimerOverlay", "Sounds");

        /// <summary>Full path of an imported sound's file.</summary>
        public static string PathFor(SoundInfo sound) => Path.Combine(FolderPath, sound.FileName);

        /// <summary>
        /// Lists the custom sound, if any. When more than one file is in the folder, keeps
        /// <paramref name="preferredKey"/> when it still exists (else the newest) and deletes the rest.
        /// </summary>
        public IReadOnlyList<SoundInfo> List(string preferredKey)
        {
            var files = AudioFiles();
            if (files.Count > 1)
            {
                var preferredName = SoundCatalog.CustomFileNameFromKey(preferredKey);
                var keep = files.FirstOrDefault(f => string.Equals(f.Name, preferredName, StringComparison.OrdinalIgnoreCase))
                           ?? files.OrderByDescending(f => f.CreationTimeUtc).First();
                DeleteExcept(keep.Name);
                files = new List<FileInfo> { keep };
            }

            return files.Select(f => SoundCatalog.Custom(f.Name)).ToList();
        }

        /// <summary>
        /// Opens a file picker and copies the chosen file into the sounds folder, replacing any custom
        /// sound already stored. Returns null if the user cancels. Throws
        /// <see cref="InvalidOperationException"/> when the file is over 50 MB or longer than 5 seconds.
        /// </summary>
        /// <param name="ownerHwnd">Window the picker belongs to — required for a desktop (non-UWP) app.</param>
        public async Task<SoundInfo?> ImportAsync(IntPtr ownerHwnd)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
            InitializeWithWindow.Initialize(picker, ownerHwnd);
            foreach (var ext in SupportedExtensions) picker.FileTypeFilter.Add(ext);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return null;

            if (new FileInfo(file.Path).Length > MaxFileBytes)
                throw new InvalidOperationException("That file is larger than 50 MB.");

            var duration = await GetDurationAsync(file.Path);
            if (duration <= TimeSpan.Zero)
                throw new InvalidOperationException("Couldn't read the length of that sound.");
            if (duration > MaxDuration)
                throw new InvalidOperationException("That sound is longer than 5 seconds.");

            Directory.CreateDirectory(FolderPath);
            var target = UniquePath(file.Name);
            File.Copy(file.Path, target);
            var name = Path.GetFileName(target);
            DeleteExcept(name);
            return SoundCatalog.Custom(name);
        }

        public void Delete(SoundInfo sound)
        {
            if (sound == null || !sound.IsCustom) return;

            // A crafted key like "custom:..\x.wav" must not reach outside the sounds folder.
            var path = Path.GetFullPath(PathFor(sound));
            if (!IsInFolder(path)) return;
            try { File.Delete(path); }
            catch { /* locked — the next import or list retries */ }
        }

        private static async Task<TimeSpan> GetDurationAsync(string path)
        {
            try
            {
                using var source = MediaSource.CreateFromUri(new Uri(path));
                await source.OpenAsync();
                return source.Duration ?? TimeSpan.Zero;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private static string UniquePath(string fileName)
        {
            var path = Path.Combine(FolderPath, fileName);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            for (var i = 2; File.Exists(path); i++)
                path = Path.Combine(FolderPath, $"{stem} ({i}){ext}");
            return path;
        }

        private void DeleteExcept(string keepFileName)
        {
            foreach (var file in AudioFiles())
            {
                if (string.Equals(file.Name, keepFileName, StringComparison.OrdinalIgnoreCase)) continue;
                try { file.Delete(); }
                catch { /* leave a locked file; the next import or list retries */ }
            }
        }

        private static List<FileInfo> AudioFiles()
        {
            var dir = new DirectoryInfo(FolderPath);
            if (!dir.Exists) return new List<FileInfo>();
            return dir.GetFiles()
                .Where(f => SupportedExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        private static bool IsInFolder(string fullPath)
        {
            var root = Path.GetFullPath(FolderPath).TrimEnd('\\') + "\\";
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }
}
