using Nternal.Extensions;
using Nternal.Structs;
using System;
using System.Collections.Generic;
using System.Text;
using Nternal.Ntfs.Attributes;

namespace Nternal.Attributes
{
    [Flags]
    public enum FileNameNamespace : byte
    {
        Posix = 0,
        Win32 = 1,
        Dos = 2,
        Win32AndDos = Win32 | Dos
    }

    public class FileNameAttribute : IParsableAttribute
    {
        public NtfsAttribute Underlying { get; init; } = null!;
        public FileNameData FileNameData { get; private set; }

        public void ParseFromAccessor(bool paranoid)
        {
            Span<byte> data = new byte[Underlying.Accessor.AllocatedLength];
            Underlying.Accessor.ReadExactly(0, data);
            
            FileNameData = new();
            FileNameData.Parse(data, paranoid);
        }
    }
}
