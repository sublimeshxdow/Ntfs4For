using Nternal.Extensions;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Nternal.Ntfs.Attributes;

namespace Nternal.Attributes
{
    [Flags]
    public enum ReparseTypeAndFlags : uint
    {
        None = 0,
        IsReserved = 0x10000000,
        IsAlias = 0x20000000,        // official name: "Name Surrogate" (IsReparseTagNameSurrogate)
        IsHighLatency = 0x40000000,
        IsMicrosoft = 0x80000000,

        MountPoint = 0xA0000003,     // was 0x88000003 -- missing the Name Surrogate bit entirely
        HSM = 0xC0000004,            // was 0xA8000004
        HSM2 = 0x80000006,
        SIS = 0x80000007,            // was 0x68000007
        WIM = 0x80000008,
        DFS = 0x8000000A,            // was 0x68000008
        SymbolicLink = 0xA000000C,   // was 0xE8000000 -- wrong M/N bits AND wrong low-16 kind value
        DFSR = 0x80000012,
        Deduplication = 0x80000013,
        NFS = 0x80000014,
        FilePlaceholder = 0x80000015,
        WOF = 0x80000017,
        Cloud = 0x9000001A,
    }

    public class ReparseData
    {
        public byte[] RawData { get; set; }
    }

    public class NameSurrogateReparseData : ReparseData
    {
        public string SubstituteName { get; set; }
        public string PrintName { get; set; }
    }

    public class SymbolicLinkReparseData : NameSurrogateReparseData
    {
        [Flags]
        public enum SymlinkFlags : uint
        {
            None,
            IsRelative = 1
        }

        public SymlinkFlags Flags { get; set; }
    }

    public class MountPointReparseData : NameSurrogateReparseData
    {

    }

    public class ReparseAttribute(NtfsAttribute underlying) : IParsableAttribute
    {
        public NtfsAttribute Underlying { get; init; } = null!;

        public void ParseFromAccessor(bool paranoid)
        {
            Span<byte> buffer = new byte[Underlying.Accessor.AllocatedLength];
            Underlying.Accessor.ReadExactly(0, buffer);

            TypeAndFlags = (ReparseTypeAndFlags)buffer.ReadUInt32();
            ushort reparseLength = buffer.ReadUInt16(4);

            int offset = 8;
            if (!TypeAndFlags.HasFlag(ReparseTypeAndFlags.IsMicrosoft))
            {
                Guid = new Guid(buffer.Slice(8, 16));
                offset += 16;
            }

            Span<byte> reparseData = new byte[reparseLength];
            buffer.Slice(offset, reparseLength).CopyTo(reparseData);
            ParseReparseData(reparseData);
        }

        void ParseReparseData(Span<byte> reparseData)
        {
            bool isSymlink = TypeAndFlags == ReparseTypeAndFlags.SymbolicLink;

            switch (TypeAndFlags)
            {
                case ReparseTypeAndFlags.MountPoint:
                case ReparseTypeAndFlags.SymbolicLink:
                    ushort substNameOffset = reparseData.ReadUInt16(0);
                    ushort substNameLength = reparseData.ReadUInt16(2);
                    ushort printNameOffset = reparseData.ReadUInt16(4);
                    ushort printNameLength = reparseData.ReadUInt16(6);
                    var symlinkFlag = isSymlink ? (SymbolicLinkReparseData.SymlinkFlags?)reparseData.ReadUInt32(8) : null;

                    if (symlinkFlag != null)
                        Data = new SymbolicLinkReparseData
                        {
                            SubstituteName = Encoding.Unicode.GetString(reparseData.Slice(12 + substNameOffset, substNameLength)),
                            PrintName = Encoding.Unicode.GetString(reparseData.Slice(12 + printNameOffset, printNameLength)),
                            Flags = symlinkFlag.Value
                        };

                    else
                        Data = new MountPointReparseData
                        {
                            SubstituteName = Encoding.Unicode.GetString(reparseData.Slice(8 + substNameOffset, substNameLength)),
                            PrintName = Encoding.Unicode.GetString(reparseData.Slice(8 + printNameOffset, printNameLength))
                        };
                    break;

                default:
                    Data = new ReparseData();
                    break;
            }

            Data.RawData = reparseData.ToArray();
        }

        public ReparseTypeAndFlags TypeAndFlags { get; set; }
        public Guid? Guid { get; set; }
        public ReparseData Data { get; set; }
    }
}
