using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Structs
{
    [Flags]
    public enum FileRecordFlags : ushort
    {
        IsInUse = 0x0001,
        IsDirectory = 0x0002
    }
}
