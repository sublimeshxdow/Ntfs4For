using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Attributes
{
    public class StandardInformationAttribute : NtfsAttribute
    {
        public override void ParseFromAccessor(bool paranoid)
        {
            Span<byte> data = new byte[Accessor.Length];
            Accessor.ReadExactly(0, data);

            FileCreation = DateTime.FromFileTimeUtc(BitConverter.ToInt64(data.Slice(0, 8)));
            FileAltered = DateTime.FromFileTimeUtc(BitConverter.ToInt64(data.Slice(8, 8)));
            MftChanged = DateTime.FromFileTimeUtc(BitConverter.ToInt64(data.Slice(16, 8)));
            FileRead = DateTime.FromFileTimeUtc(BitConverter.ToInt64(data.Slice(24, 8)));
            Attributes = (FileFlags)BitConverter.ToUInt32(data.Slice(32, 4));
            MaxNumberOfVersions = BitConverter.ToUInt32(data.Slice(36, 4));
            VersionNumber = BitConverter.ToUInt32(data.Slice(40, 4));
            ClassId = BitConverter.ToUInt32(data.Slice(44, 4));

            if (Accessor.Length >= 52)
                OwnerId = BitConverter.ToUInt32(data.Slice(48, 4));
            if (Accessor.Length >= 56)
                SecurityId = BitConverter.ToUInt32(data.Slice(52, 4));
            if (Accessor.Length >= 64)
                QuotaCharged = BitConverter.ToUInt64(data.Slice(56, 8));
            if (Accessor.Length >= 72)
                UpdateSequenceNumber = BitConverter.ToUInt64(data.Slice(64, 8));
        }

        public DateTime FileCreation { get; set; }
        public DateTime FileAltered { get; set; }
        public DateTime MftChanged { get; set; }
        public DateTime FileRead { get; set; }
        public FileFlags Attributes { get; set; }
        public uint MaxNumberOfVersions { get; set; }
        public uint VersionNumber { get; set; }
        public uint ClassId { get; set; }
        public uint? OwnerId { get; set; }
        public uint? SecurityId { get; set; }
        public ulong? QuotaCharged { get; set; }
        public ulong? UpdateSequenceNumber { get; set; }
    }
}
