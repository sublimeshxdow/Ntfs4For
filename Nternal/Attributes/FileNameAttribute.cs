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

    public class FileNameAttribute : NtfsAttribute
    {
        public FileNameData FileNameData { get; private set; }

        public override void ParseFromAccessor(bool paranoid)
        {
            Span<byte> data = new byte[Accessor.Length];
            Accessor.ReadExactly(0, data);
            
            FileNameData = new();
            FileNameData.Parse(data, paranoid);
        }
    }
}
