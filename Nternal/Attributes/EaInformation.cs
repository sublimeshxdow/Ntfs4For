using Nternal.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal
{
    public class EaInformationAttribute : NtfsAttribute
    {
        public ushort SizeOfPackedEAs { get; set; }
        public ushort NumberOfNeededEAs { get; set; }
        public uint SizeOfUnpackedEAs { get; set; }
    }
}
