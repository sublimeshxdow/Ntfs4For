using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Transactions;
using Nternal.Attributes;
using Nternal.Core.Streams;
using Nternal.Helpers;
using Nternal.Internal;
using Nternal.Streams;
using Nternal.Structs;

namespace Nternal.Ntfs.Helpers;

public class FileRecordMerger(NtfsVolume volume, NtfsFile file, bool isMft)
{
    private readonly NtfsVolume _volume = volume;
    private readonly NtfsFile _file = file;
    private readonly bool _isMft = isMft;
    
    private readonly Dictionary<FileReference, RawFileRecord> _cachedFileRecords =
        new Dictionary<FileReference, RawFileRecord>(4);

    public void MergeListedEntry(AttributeListAttribute.Entry entry)
    {
        IDataStream? mftStream = _isMft ? _file.NtfsAttributes.First(x => x.Type == NtfsAttributeType.Data).Accessor : null;
        
        if (!_cachedFileRecords.ContainsKey(entry.BaseFileReference))
            _cachedFileRecords[entry.BaseFileReference] = _volume.GetRawRecordData(entry.BaseFileReference, mft: mftStream);
        
        var foreignRecord = _cachedFileRecords[entry.BaseFileReference];

        // Look for attribute
        Span<byte> attributeBuffer = foreignRecord.AttributeData;

        while (true)
        {
            var type = (NtfsAttributeType)BinaryPrimitives.ReadUInt32LittleEndian(attributeBuffer);
            if (type == NtfsAttributeType.EndMarker)
                break;

            RawAttributeHeader header = MemoryMarshal.Cast<byte, RawAttributeHeader>(attributeBuffer)[0];
            string name = Encoding.Unicode.GetString(attributeBuffer.Slice(header.NameOffset, header.NameLength));

            if (entry.AttributeId != header.AttributeId
                || entry.Type != header.Type
                || entry.Name != name)
            {
                attributeBuffer = attributeBuffer.Slice((int)header.Length);
                continue;
            }
            
            // We found the matching record
            if (header.NonResidentFlag == 0)
            {
                RawResidentAttributeHeader residentHeader = MemoryMarshal.Cast<byte, RawResidentAttributeHeader>(attributeBuffer)[0];
                ReadOnlySpan<byte> data = attributeBuffer.Slice(residentHeader.AttributeOffset, (int)residentHeader.AttributeLength);
                MergeResidentAttribute(residentHeader, name, data);
            }

            else
            {
                RawNonResidentAttributeHeader nonResidentHeader = MemoryMarshal.Cast<byte, RawNonResidentAttributeHeader>(attributeBuffer)[0];
                ReadOnlySpan<byte> dataRuns = attributeBuffer.Slice(nonResidentHeader.DataRunsOffset, (int)(header.Length - nonResidentHeader.DataRunsOffset));
                MergeNonResidentAttribute(nonResidentHeader, name, dataRuns);
            }

            return;
        }

        throw new Exception($"Could not find an instance of attribute {entry} within record-reference {foreignRecord}!");
    }

    private void MergeResidentAttribute(RawResidentAttributeHeader header, string name, ReadOnlySpan<byte> data)
    {
        if (IsAttributePresent(header, name))
            return;
        
        // Life is easy - we simply append the attribute to our file, if it's not present yet
        _file.NtfsAttributes.Add(new NtfsAttribute()
        {
            Type = header.Common.Type,
            IsNonResident = false,
            Flags = header.Common.Flags,
            Id = header.Common.AttributeId,
            Name = name,

            Accessor = new ResidentDataStream(data.ToArray()),
            BytesPerCluster = _volume.BootSector.BytesPerCluster
        });
    }

    private void MergeNonResidentAttribute(RawNonResidentAttributeHeader header, string name, ReadOnlySpan<byte> dataruns)
    {
        if (IsAttributePresent(header, name))
            return;
        
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
                
                Accessor = new NonResidentDataStream(_volume.GetUnderlyingStream(), _volume.BootSector.BytesPerCluster),
                BytesPerCluster = _volume.BootSector.BytesPerCluster
            };
                
            ((NonResidentDataStream)attribute.Accessor).AddExtent(extent);
            _file.NtfsAttributes.Add(attribute);
        }
    }

    private bool IsAttributePresent(RawResidentAttributeHeader header, string name)
    {
        return file.NtfsAttributes
            .Where(x => !x.IsNonResident)
            .Where(x => x.Type == header.Common.Type)
            .Where(x => x.Id == header.Common.AttributeId)
            .FirstOrDefault(x => x.Name == name) != null;
    }

    private bool IsAttributePresent(RawNonResidentAttributeHeader header, string name)
    {
        return file.NtfsAttributes
            .Where(x => x.IsNonResident)
            .Where(x => x.Type == header.Common.Type)
            .Where(x => x.Id == header.Common.AttributeId)
            .Where(x => ((NonResidentDataStream)x.Accessor).Extents.Any(x => x.LowestVcn == header.StartingVcn))
            .FirstOrDefault(x => x.Name == name) != null;
    }

    private NtfsAttribute? GetMatchingAttribute(RawNonResidentAttributeHeader header, string name)
    {
        return file.NtfsAttributes
            .Where(x => x.Type == header.Common.Type)
            .Where(x => x.Id == header.Common.AttributeId)
            .FirstOrDefault(x => x.Name == name);
    }
}
