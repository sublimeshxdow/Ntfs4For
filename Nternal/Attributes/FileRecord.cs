using Nternal.Internal;
using Nternal.Streams;
using Nternal.Structs;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Nternal.Attributes
{
    public class FileRecord : RawFileRecord
    {
        public List<NtfsAttribute> NtfsAttributes = [];

        public FileRecord(ReadOnlySpan<byte> buffer, bool postXP = true) : base(buffer, postXP)
        {
            ushort offsetToAttribute = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0x14, 2));
            ReadOnlySpan<byte> attributeBuffer = buffer.Slice(offsetToAttribute, buffer.Length - offsetToAttribute);

            while (true)
            {
                var type = (NtfsAttributeType)BinaryPrimitives.ReadUInt32LittleEndian(attributeBuffer);
                if (type == NtfsAttributeType.EndMarker)
                    break;

                RawAttributeHeader header = MemoryMarshal.Cast<byte, RawAttributeHeader>(attributeBuffer)[0];
                string name = Encoding.Unicode.GetString(attributeBuffer.Slice(header.NameOffset, header.NameLength));

                if (header.NonResidentFlag == 0)
                {
                    RawResidentAttributeHeader residentHeader = MemoryMarshal.Cast<byte, RawResidentAttributeHeader>(attributeBuffer)[0];
                    ReadOnlySpan<byte> data = attributeBuffer.Slice(residentHeader.AttributeOffset, (int)residentHeader.AttributeLength);
                    ParseResidentAttribute(residentHeader, name, data);
                }

                else
                {
                    RawNonResidentAttributeHeader nonResidentHeader = MemoryMarshal.Cast<byte, RawNonResidentAttributeHeader>(attributeBuffer)[0];
                    ReadOnlySpan<byte> dataRuns = attributeBuffer.Slice(nonResidentHeader.DataRunsOffset, (int)(header.Length - nonResidentHeader.DataRunsOffset));
                    ParseNonResidentAttribute(nonResidentHeader, name, dataRuns);
                }

                attributeBuffer = attributeBuffer.Slice(attributeBuffer.Length);
            }
        }

        private void ParseResidentAttribute(RawResidentAttributeHeader header, string name, ReadOnlySpan<byte> data)
        {
            NtfsAttributes.Add(new NtfsAttribute()
            {
                Type = header.Common.Type,
                IsNonResident = false,
                Flags = header.Common.Flags,
                Id = header.Common.AttributeId,
                Name = name,

                Accessor = new ResidentDataStream(data.ToArray())
            });
        }

        private void ParseNonResidentAttribute(RawNonResidentAttributeHeader header, string name, ReadOnlySpan<byte> dataruns)
        {
            throw new NotImplementedException();
        }

        private NtfsAttribute? GetMatchingAttribute(RawNonResidentAttributeHeader header, string name)
        {
            return NtfsAttributes
                .Where(x => x.Type == header.Common.Type)
                .Where(x => x.Id == header.Common.AttributeId)
                .Where(x => x.Name == name)
                .FirstOrDefault();
        }


    }
}
