using Nternal.Attributes;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Nternal.Internal
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RawAttributeHeader
    {
        public NtfsAttributeType Type;              // 0x00
        public uint Length;            // 0x04
        public byte NonResidentFlag;   // 0x08
        public byte NameLength;        // 0x09
        public ushort NameOffset;      // 0x0A
        public NtfsAttributeFlags Flags;            // 0x0C
        public ushort AttributeId;     // 0x0E
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RawResidentAttributeHeader
    {
        public RawAttributeHeader Common; // 0x00

        public uint AttributeLength;   // 0x10
        public ushort AttributeOffset; // 0x14
        public byte IndexedFlag;       // 0x16
        public byte Padding;           // 0x17

        // Attribute data starts at 0x18
        // Variable length: Common.AttributeLength
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RawNonResidentAttributeHeader
    {
        public RawAttributeHeader Common; // 0x00

        public long StartingVcn;      // 0x10
        public long LastVcn;          // 0x18
        public ushort DataRunsOffset;  // 0x20
        public ushort CompressionUnitSize; // 0x22
        public uint Padding;           // 0x24
        public long AllocatedSize;    // 0x28
        public long RealSize;         // 0x30
        public long InitializedSize;  // 0x38

        // Name / Data Runs follow depending on NameLength
    }
}
