using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Attributes
{
    [Flags]
    public enum EaFlag
    {
        None = 0x00,
        FileNeedEA = 0x80
    }

    public class EaAttribute : NtfsAttribute
    {
        public class Entry
        {
            public string Name { get; set; }
            public byte[] Value { get; set; }
            public EaFlag Flags { get; set; }
        }

        public Entry[] EAEntries;
    }
}
