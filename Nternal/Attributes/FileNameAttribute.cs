using Nternal.Extensions;
using Nternal.Structs;
using System;
using System.Collections.Generic;
using System.Text;

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
        public override void ParseFromAccessor(bool paranoid)
        {
            Span<byte> data = new byte[Accessor.Length];
            Accessor.ReadExactly(0, data);

            ParentFolder = new FileReference(data.ReadUInt64(0));
            FileCreation = DateTime.FromFileTimeUtc(data.ReadInt64(8));
            FileAltered = DateTime.FromFileTimeUtc(data.ReadInt64(16));
            MftChanged = DateTime.FromFileTimeUtc(data.ReadInt64(24));
            FileRead = DateTime.FromFileTimeUtc(data.ReadInt64(32));

            AllocatedSizeOfFile = data.ReadInt64(40);
            RealSize = data.ReadInt64(48);
            Flags = (FileFlags)data.ReadUInt32(56);
            ExtendedData = data.ReadUInt32(60);

            int nameLength = data[64];
            FileNamespace = (FileNameNamespace)data[65];

            Name = Encoding.Unicode.GetString(data.Slice(66, nameLength * 2));
        }

        public FileReference ParentFolder { get; set; }
        public DateTime FileCreation { get; set; }
        public DateTime FileAltered { get; set; }
        public DateTime MftChanged { get; set; }
        public DateTime FileRead { get; set; }

        public long AllocatedSizeOfFile { get; set; }
        public long RealSize { get; set; }
        public FileFlags Flags { get; set; }
        public uint ExtendedData { get; set; }
        public FileNameNamespace FileNamespace { get; set; }
        public string Name { get; set; }
    }
}
