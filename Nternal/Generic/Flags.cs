namespace Nternal.Ntfs.Generic;

[Flags]
public enum FileRecordFlags : ushort
{
    IsInUse = 0x0001,
    IsDirectory = 0x0002
}