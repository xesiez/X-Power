using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;

namespace XPower.Storage
{
    /// <summary>
    /// A user-visible storage location. This describes an accessible WinRT root;
    /// it does not imply unrestricted access to the underlying volume.
    /// </summary>
    public sealed class StorageRoot
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public StorageFolder Folder { get; private set; }
        public StorageRootKind Kind { get; private set; }

        public StorageRoot(string id, string name, string description, StorageFolder folder, StorageRootKind kind)
        {
            Id = id;
            Name = name;
            Description = description;
            Folder = folder;
            Kind = kind;
        }
    }

    public enum StorageRootKind
    {
        AppPrivate,
        Removable
    }

    /// <summary>
    /// Owns discovery of roots exposed to this UWP app. Keep platform access here,
    /// rather than mixing it with page rendering and selection state.
    /// </summary>
    public sealed class ExplorerStorageService
    {
        public async Task<IReadOnlyList<StorageRoot>> GetRootsAsync()
        {
            var roots = new List<StorageRoot>();

            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            roots.Add(new StorageRoot(
                "app-local",
                "Internal storage",
                "Private X-Power app storage",
                localFolder,
                StorageRootKind.AppPrivate));

            try
            {
                IReadOnlyList<StorageFolder> removableFolders =
                    await KnownFolders.RemovableDevices.GetFoldersAsync();

                foreach (StorageFolder folder in removableFolders)
                {
                    if (folder == null)
                        continue;

                    string rootIdentity = String.IsNullOrWhiteSpace(folder.Path)
                        ? Uri.EscapeDataString(folder.Name)
                        : Uri.EscapeDataString(folder.Path);
                    roots.Add(new StorageRoot(
                        "removable:" + rootIdentity,
                        folder.Name,
                        "Removable USB storage",
                        folder,
                        StorageRootKind.Removable));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Keep app-private storage available when removable access is denied.
            }
            catch (System.IO.IOException)
            {
                // A disconnected or unavailable device must not prevent app startup.
            }

            return roots.AsReadOnly();
        }

        public Task<IReadOnlyList<StorageFolder>> GetFoldersAsync(StorageFolder folder)
        {
            if (folder == null)
                throw new ArgumentNullException("folder");

            return GetFoldersCoreAsync(folder);
        }

        private static async Task<IReadOnlyList<StorageFolder>> GetFoldersCoreAsync(StorageFolder folder)
        {
            IReadOnlyList<StorageFolder> folders = await folder.GetFoldersAsync();
            return folders;
        }

        public Task<IReadOnlyList<StorageFile>> GetFilesAsync(StorageFolder folder)
        {
            if (folder == null)
                throw new ArgumentNullException("folder");

            return GetFilesCoreAsync(folder);
        }

        private static async Task<IReadOnlyList<StorageFile>> GetFilesCoreAsync(StorageFolder folder)
        {
            IReadOnlyList<StorageFile> files = await folder.GetFilesAsync();
            return files;
        }
    }
}
