using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.NameCollisionOption;

namespace XPower.Storage
{
    /// <summary>
    /// File mutations are isolated from page code. Each operation receives explicit
    /// source and destination handles; no implicit drive paths or elevated access.
    /// </summary>
    public sealed class FileOperationsService
    {
        public Task<StorageFile> CopyFileAsync(StorageFile source, StorageFolder destination)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (destination == null)
                throw new ArgumentNullException("destination");

            return source.CopyAsync(
                destination,
                source.Name,
                NameCollisionOption.GenerateUniqueName).AsTask();
        }
    }
}
