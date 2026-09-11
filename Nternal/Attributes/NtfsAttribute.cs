using Nternal.Streams;
using Nternal.Core.Streams;
using System;
using System.Collections.Generic;
using System.Text;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;

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
