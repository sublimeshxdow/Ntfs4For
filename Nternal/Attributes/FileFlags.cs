using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Attributes
{
    [Flags]
    public enum DosFilePermissions : uint
    {
        ReadOnly = 0x0001,
        Hidden = 0x0002,
        System = 0x0004,
        Archive = 0x0020,
        Device = 0x0040,
        Normal = 0x0080,
        Temporary = 0x0100,
        SparseFile = 0x0200,
        ReparsePoint = 0x0400,
        Compressed = 0x0800,
        Offline = 0x1000,
        NotContentIndexed = 0x2000,
        Encrypted = 0x4000
    }

    [Flags]
    public enum FileFlags : uint
    {
        ReadOnly = 0x0001,
        Hidden = 0x0002,
        System = 0x0004,
        Archive = 0x0020,
        Device = 0x0040,
        Normal = 0x0080,
        Temporary = 0x0100,
        SparseFile = 0x0200,
        ReparsePoint = 0x0400,
        Compressed = 0x0800,
        Offline = 0x1000,
        NotContentIndexed = 0x2000,
        Encrypted = 0x4000,

        Directory = 0x10000000,
        IndexView = 0x20000000
    }
}
