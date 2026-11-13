using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace XPower.Storage
{
    /// <summary>
    /// File mutations are isolated from page code. Each operation receives explicit
    /// source and destination handles; no implicit drive paths or elevated access.
    /// </summary>
    public sealed class FileOperationsService
    {
        public async Task<StorageFile> CopyFileAsync(StorageFile source, StorageFolder destination)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (destination == null)
                throw new ArgumentNullException("destination");

            return await source.CopyAsync(
                destination,
                source.Name,
                NameCollisionOption.GenerateUniqueName);
        }
    }
}
