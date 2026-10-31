using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IntervalTimer.Core;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace IntervalTimerWidget
{
    /// <summary>
    /// Imports one user-supplied audio file into the widget's local storage. The <c>Sounds</c>
    /// subfolder of <see cref="ApplicationData.LocalFolder"/> holds at most that one file — importing
    /// another deletes the previous custom file. Packaged defaults under <c>Assets\Sounds</c>
    /// (Beep, Double beep, Chime, Bell, Alarm) are never deleted and stay in the picker.
    /// </summary>
    internal sealed class CustomSoundStore
    {
        private const string SubFolderName = "Sounds";
        private const long MaxFileBytes = 50L * 1024 * 1024;
        private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(5);

        // Formats Windows.Media.Playback.MediaPlayer decodes natively, no extra codec needed.
        private static readonly string[] SupportedExtensions =
            { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac" };

        /// <summary>
        /// Lists the custom sound, if any. When more than one file is already in the folder
        /// (an older build allowed that), keeps <paramref name="preferredKey"/> when it still
        /// exists and deletes the rest.
        /// </summary>
        public async Task<IReadOnlyList<SoundInfo>> ListAsync(string preferredKey)
        {
            var files = await AudioFilesAsync();
            if (files.Count > 1)
            {
                var preferredName = SoundCatalog.CustomFileNameFromKey(preferredKey);
                var keep = files.FirstOrDefault(f =>
                               string.Equals(f.Name, preferredName, StringComparison.OrdinalIgnoreCase))
                           ?? files.OrderByDescending(f => f.DateCreated).First();
                await DeleteExceptAsync(keep.Name);
                files = new List<StorageFile> { keep };
            }

            return files.Select(f => SoundCatalog.Custom(f.Name)).ToList();
        }

        /// <summary>
        /// Opens a file picker and copies the chosen file into local storage, replacing any custom
        /// sound already stored. Returns null if the user cancels. Throws
        /// <see cref="InvalidOperationException"/> when the file is over 50 MB or longer than 5 seconds.
        /// </summary>
        public async Task<SoundInfo> ImportAsync()
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
            foreach (var ext in SupportedExtensions) picker.FileTypeFilter.Add(ext);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return null;

            var props = await file.GetBasicPropertiesAsync();
            if (props.Size > MaxFileBytes)
                throw new InvalidOperationException("That file is larger than 50 MB.");

            var duration = await GetDurationAsync(file);
            if (duration <= TimeSpan.Zero)
                throw new InvalidOperationException("Couldn't read the length of that sound.");
            if (duration > MaxDuration)
                throw new InvalidOperationException("That sound is longer than 5 seconds.");

            var folder = await GetFolderAsync();
            var copy = await file.CopyAsync(folder, file.Name, NameCollisionOption.GenerateUniqueName);
            await DeleteExceptAsync(copy.Name);
            return SoundCatalog.Custom(copy.Name);
        }

        public async Task DeleteAsync(SoundInfo sound)
        {
            if (sound == null || !sound.IsCustom) return;
            if (SoundCatalog.All.Any(s => string.Equals(s.FileName, sound.FileName, StringComparison.OrdinalIgnoreCase)
                                          && string.Equals(s.Key, sound.Key, StringComparison.OrdinalIgnoreCase)))
                return;

            var folder = await GetFolderAsync();
            try
            {
                var file = await folder.GetFileAsync(sound.FileName);
                if (!CanDelete(file, folder)) return;
                await file.DeleteAsync();
            }
            catch (System.IO.FileNotFoundException)
            {
                // Already gone — nothing to do.
            }
        }

        private async Task DeleteExceptAsync(string keepFileName)
        {
            var customFolder = await GetFolderAsync();
            foreach (var file in await AudioFilesAsync())
            {
                if (string.Equals(file.Name, keepFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!CanDelete(file, customFolder))
                    continue;
                try { await file.DeleteAsync(); }
                catch { /* leave a locked file; the next import or list retries */ }
            }
        }

        /// <summary>
        /// Only a file inside the local custom-sound folder can be removed. A packaged default
        /// under the install directory's <c>Assets\Sounds</c> is left alone even when the file name matches.
        /// </summary>
        private static bool CanDelete(StorageFile file, StorageFolder customFolder)
        {
            if (file == null || customFolder == null) return false;

            var customRoot = customFolder.Path.TrimEnd('\\') + "\\";
            if (!file.Path.StartsWith(customRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            var installed = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
            if (!string.IsNullOrEmpty(installed)
                && file.Path.StartsWith(installed.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                return false;

            if (file.Path.IndexOf(@"\Assets\Sounds\", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return true;
        }

        private static async Task<TimeSpan> GetDurationAsync(StorageFile file)
        {
            try
            {
                var clip = await MediaClip.CreateFromFileAsync(file);
                return clip.OriginalDuration;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private async Task<List<StorageFile>> AudioFilesAsync()
        {
            var folder = await GetFolderAsync();
            var files = await folder.GetFilesAsync();
            return files.Where(f => IsSupported(f.Name)).ToList();
        }

        private static bool IsSupported(string fileName)
        {
            var ext = System.IO.Path.GetExtension(fileName);
            return SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        private static async Task<StorageFolder> GetFolderAsync() =>
            await ApplicationData.Current.LocalFolder.CreateFolderAsync(SubFolderName, CreationCollisionOption.OpenIfExists);
    }
}
