using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Structs
{
    public class GenericFileRecord
    {
        public GenericFileRecord(ReadOnlySpan<byte> buffer, bool postXp = true)
        {
            if (buffer.Length < 0x30)
                throw new ArgumentException("Buffer must contain at least 48 bytes.", nameof(buffer));

            MagicNumber = Encoding.ASCII.GetString(buffer.Slice(0x00, 4));
            Lsn = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(0x08, 8));
            SequenceNumber = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0x10, 2));
            NumberOfHardlinks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0x12, 2));
            OffsetToAttributes = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0x14, 2));
            Flags = (FileRecordFlags)BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0x16, 2));
            RealSizeOfRecord = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0x18, 4));
            AllocatedSizeOfRecord = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0x1C, 4));
            BaseFileReference = new FileReference(BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(0x20, 8)));
            NextAttributeId = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0x28, 2));

            if (postXp)
                NumberOfMftRecord = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0x2C, 4));
        }

        public string MagicNumber { get; } = "";
        public ulong Lsn { get; }
        public ushort SequenceNumber { get; }
        public ushort NumberOfHardlinks { get; }
        public ushort OffsetToAttributes { get; }
        public FileRecordFlags Flags { get; }
        public uint RealSizeOfRecord { get; }
        public uint AllocatedSizeOfRecord { get; }
        public FileReference BaseFileReference { get; }
        public ushort NextAttributeId { get; }
        public uint? NumberOfMftRecord { get; }
    }
}
