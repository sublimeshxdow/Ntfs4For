using Nternal.Internal;
using Nternal.Streams;
using Nternal.Structs;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Nternal.Core.Streams;
using Nternal.Helpers;

namespace Nternal.Attributes
{
    public class NtfsFile : GenericFileRecord
    {
        public List<NtfsAttribute> NtfsAttributes = [];

        public NtfsFile(ReadOnlySpan<byte> buffer, IDataStream underlying, bool postXP = true) : base(buffer, postXP)
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
                    ParseNonResidentAttribute(nonResidentHeader, name, dataRuns, underlying);
                }

                attributeBuffer = attributeBuffer.Slice((int)header.Length);
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

        private void ParseNonResidentAttribute(RawNonResidentAttributeHeader header, string name, ReadOnlySpan<byte> dataruns, IDataStream underlying)
        {
            var extent = RunHelpers.GetExtentOfNrh(header, dataruns);

            var matchingAttribute = GetMatchingAttribute(header, name);
            if (matchingAttribute != null)
            {
                var stream = matchingAttribute.Accessor as NonResidentDataStream;
                if (stream == null)
                    throw new InvalidOperationException(
                        $"Found an existing, corresponding attribute to the one attempted to parse"
                        + $" (Type={matchingAttribute.Type}, Name={matchingAttribute.Name}, <=> Type={header.Common.Type}, Name={name})"
                        + $", but the found accessor is not a NonResidentDataStream, but of type {matchingAttribute.Accessor.GetType().ToString()} - this should never happen on a healthy volume!");
                
                stream.AddExtent(extent);
            }
            else
            {
                var attribute = new NtfsAttribute()
                {
                    Type = header.Common.Type,
                    IsNonResident = true,
                    Flags = header.Common.Flags,
                    Id = header.Common.AttributeId,
                    Name = name,

                    Accessor = new NonResidentDataStream(underlying)
                };
                
                ((NonResidentDataStream)attribute.Accessor).AddExtent(extent);
                NtfsAttributes.Add(attribute);
            }
        }

        private NtfsAttribute? GetMatchingAttribute(RawNonResidentAttributeHeader header, string name)
        {
            return NtfsAttributes
                .Where(x => x.Type == header.Common.Type)
                .Where(x => x.Id == header.Common.AttributeId)
                .FirstOrDefault(x => x.Name == name);
        }
    }
}
