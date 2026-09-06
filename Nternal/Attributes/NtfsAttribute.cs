using Nternal.Streams;
using Nternal.Core.Streams;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Attributes
{
    public class NtfsAttribute
    {
        public NtfsAttributeType Type { get; init; }
        public bool IsNonResident { get; init; }
        public NtfsAttributeFlags Flags { get; init; }
        public ushort Id { get; init; }
        public string Name { get; init; } = "";

        public required IDataStream Accessor { get; set; }

        public virtual void ParseFromAccessor(bool paranoid)
        {
            throw new NotImplementedException();
        }
    }
}
