using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Generic;

namespace Nternal.Ntfs;

public class FolderEntry : FileNameData
{
    public FolderEntry(FileReference reference, ReadOnlySpan<byte> fileNameDataBuffer)
    {
        Reference = reference;
        Parse(fileNameDataBuffer, true);
    }

    public FileReference Reference { get; init; }
}