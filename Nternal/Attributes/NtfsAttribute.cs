using Nternal.Core;
using Nternal.Core.Streams;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Streams;
using Nternal.Structs;
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
        public string Name { get; init; } = "";

        internal AttributeSource? ResidentSource { get; init; }
        public AttributeSource Source => IsNonResident ?
            ((NonResidentDataStream)Accessor).Extents.First().Source : ResidentSource!.Value;

        public required IDataStream Accessor { get; set; }
        public int BytesPerCluster { get; init; }

        public T Cast<T>(bool paranoid = true) where T : IParsableAttribute, new()
        {
            var attribute = new T
            {
                Underlying = this
            };
            attribute.ParseFromAccessor(paranoid);
            return attribute;
        }
    }
}
