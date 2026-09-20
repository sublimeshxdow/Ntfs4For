using Nternal.Ntfs.Attributes;
using Nternal.Structs;

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
