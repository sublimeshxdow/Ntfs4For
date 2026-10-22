using Nternal.Ntfs.Generic;

namespace Nternal.Ntfs.NtfsExtensions.Journal;

public interface IUsnEntry
{
    ushort MajorVersion { get; }
    ushort MinorVersion { get; }
    FileReference FileReference { get; }
    FileReference ParentFileReference { get; }
    ulong Usn { get; }
    UsnReason Reason { get; }
    UsnSourceInfo SourceInfo { get; }
}