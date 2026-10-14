using Nternal.Core.Streams;
using Nternal.Extensions;
using Nternal.Ntfs.Attributes;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nternal.Attributes
{
    public class LoggedUtilityStreamAttribute : IParsableAttribute
    {
        public static NtfsAttributeType AttributeType => NtfsAttributeType.LoggedUtilityStream;

        public NtfsAttribute Underlying { get; init; } = null!;

        public void ParseFromAccessor(bool paranoid)
        { }

        public IDataStream GetStream() => Underlying.Accessor;
    }
}