using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IntervalTimer.Core;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace IntervalTimerWidget
{
    /// <summary>
    /// Imports user-supplied audio files into the widget's local storage, and lists/removes them.
    /// The <c>Sounds</c> subfolder of <see cref="ApplicationData.LocalFolder"/> is the only source
    /// of truth — there's no separate index that can fall out of sync with the files on disk.
    /// </summary>
    internal sealed class CustomSoundStore
    {
        private const string SubFolderName = "Sounds";
        private const long MaxFileBytes = 5 * 1024 * 1024;

        // Formats Windows.Media.Playback.MediaPlayer decodes natively, no extra codec needed.
        private static readonly string[] SupportedExtensions =
            { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac" };

        public async Task<IReadOnlyList<SoundInfo>> ListAsync()
        {
            var folder = await GetFolderAsync();
            var files = await folder.GetFilesAsync();
            return files
                .Where(f => SupportedExtensions.Contains(System.IO.Path.GetExtension(f.Name).ToLowerInvariant()))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => SoundCatalog.Custom(f.Name))
                .ToList();
        }

        /// <summary>
        /// Opens a file picker and copies the chosen file into local storage. Returns null if the
        /// user cancels. Throws <see cref="InvalidOperationException"/> if the file is too large.
        /// </summary>
        public async Task<SoundInfo> ImportAsync()
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
            foreach (var ext in SupportedExtensions) picker.FileTypeFilter.Add(ext);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return null;

            var props = await file.GetBasicPropertiesAsync();
            if (props.Size > MaxFileBytes)
                throw new InvalidOperationException("That file is larger than 5 MB.");

            var folder = await GetFolderAsync();
            var copy = await file.CopyAsync(folder, file.Name, NameCollisionOption.GenerateUniqueName);
            return SoundCatalog.Custom(copy.Name);
        }

        public async Task DeleteAsync(SoundInfo sound)
        {
            if (sound == null || !sound.IsCustom) return;
            var folder = await GetFolderAsync();
            try
            {
                var file = await folder.GetFileAsync(sound.FileName);
                await file.DeleteAsync();
            }
            catch (System.IO.FileNotFoundException)
            {
                // Already gone — nothing to do.
            }
        }

        private static async Task<StorageFolder> GetFolderAsync() =>
            await ApplicationData.Current.LocalFolder.CreateFolderAsync(SubFolderName, CreationCollisionOption.OpenIfExists);
    }
}
