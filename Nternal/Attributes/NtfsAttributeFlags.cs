using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Attributes
{
    [Flags]
    public enum NtfsAttributeFlags : ushort
    {
        None = 0x0000,
        Compressed = 0x0001,
        Encrypted = 0x4000,
        Sparse = 0x8000
    }
}
