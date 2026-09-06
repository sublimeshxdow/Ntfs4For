using System;
using System.Collections.Generic;
using System.Text;
using Nternal.Structs;

namespace Nternal.Attributes
{
    public class AttributeListAttribute : NtfsAttribute
    {
        public class Entry
        {
            public FileReference BaseFileReference  { get; init; }

            public ushort AttributeId { get; init; }
            public NtfsAttributeType Type { get; init; }
            public string Name { get; init; }
            public long LowestVcn  { get; init; }
        }
    }
}
