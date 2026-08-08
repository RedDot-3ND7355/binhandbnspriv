using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace LegacyBin.Ava
{
    /// <summary>File/folder pickers via TopLevel StorageProvider (works on Linux GTK).</summary>
    internal static class AvaDialogs
    {
        public static async Task<string> PickOpenFileAsync(Window owner, string title, string filterName, string[] extensions)
        {
            var top = TopLevel.GetTopLevel(owner);
            if (top == null)
            {
                return null;
            }
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType(filterName) { Patterns = extensions },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            }).ConfigureAwait(true);
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        public static async Task<string> PickSaveFileAsync(Window owner, string title, string filterName, string[] extensions, string suggestedFileName)
        {
            var top = TopLevel.GetTopLevel(owner);
            if (top == null)
            {
                return null;
            }
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new FilePickerFileType(filterName) { Patterns = extensions },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            }).ConfigureAwait(true);
            return file?.TryGetLocalPath();
        }

        public static async Task<string> PickFolderAsync(Window owner, string title)
        {
            var top = TopLevel.GetTopLevel(owner);
            if (top == null)
            {
                return null;
            }
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            }).ConfigureAwait(true);
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }
    }
}
