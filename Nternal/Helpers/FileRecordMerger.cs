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

        // Wrap if unaligned
        if (mftStream != null && mftStream.Alignment != 1)
            mftStream = new UnalignedDataStream(mftStream);

        if (!_cachedFileRecords.ContainsKey(entry.BaseFileReference))
            _cachedFileRecords[entry.BaseFileReference] = _volume.GetRawRecordData(entry.BaseFileReference, isAttributeLookup: true,
                mft: mftStream, compareSequence: true);

        var foreignRecord = _cachedFileRecords[entry.BaseFileReference];

        // Look for attribute
        Span<byte> attributeBuffer = foreignRecord.AttributeData;

        while (true)
        {
            var type = (NtfsAttributeType)BinaryPrimitives.ReadUInt32LittleEndian(attributeBuffer);
            if (type == NtfsAttributeType.EndMarker)
                break;

            RawAttributeHeader header = MemoryMarshal.Cast<byte, RawAttributeHeader>(attributeBuffer)[0];

            if (entry.Id != header.AttributeId)
            {
                attributeBuffer = attributeBuffer.Slice((int)header.Length);
                continue;
            }

            string name = Encoding.Unicode.GetString(attributeBuffer.Slice(header.NameOffset, header.NameLength * 2));
            if (header.Type != entry.Type || name != entry.Name)
                throw new InvalidDataException($"Attribute list entry {entry} does not match instance {header.AttributeId} in record {entry.BaseFileReference}");

            // We found the matching record
            if (header.NonResidentFlag == 0)
            {
                RawResidentAttributeHeader residentHeader = MemoryMarshal.Cast<byte, RawResidentAttributeHeader>(attributeBuffer)[0];
                ReadOnlySpan<byte> data = attributeBuffer.Slice(residentHeader.AttributeOffset, (int)residentHeader.AttributeLength);
                MergeResidentAttribute(residentHeader, entry, data);
            }

            else
            {
                RawNonResidentAttributeHeader nonResidentHeader = MemoryMarshal.Cast<byte, RawNonResidentAttributeHeader>(attributeBuffer)[0];
                if (nonResidentHeader.StartingVcn != entry.LowestVcn)
                    throw new InvalidDataException($"Attribute list entry {entry} does not match instance {header.AttributeId} in record {entry.BaseFileReference}");

                ReadOnlySpan<byte> dataRuns = attributeBuffer.Slice(nonResidentHeader.DataRunsOffset, (int)(header.Length - nonResidentHeader.DataRunsOffset));
                MergeNonResidentAttribute(nonResidentHeader, entry, dataRuns);
            }

            return;
        }

        throw new Exception($"Could not find an instance of attribute {entry} within record-reference {entry.BaseFileReference}!");
    }

    private void MergeResidentAttribute(RawResidentAttributeHeader header, AttributeListAttribute.Entry entry, ReadOnlySpan<byte> data)
    {
        if (IsAttributePresent(header, entry))
            return;

        // Life is easy - we simply append the attribute to our file, if it's not present yet
        _file.NtfsAttributes.Add(new NtfsAttribute()
        {
            Type = header.Common.Type,
            IsNonResident = false,
            Flags = header.Common.Flags,
            ResidentSource = new(entry.BaseFileReference, header.Common.AttributeId),
            Name = entry.Name,

            Accessor = new ResidentDataStream(data.ToArray()),
            BytesPerCluster = _volume.BootSector.BytesPerCluster
        });
    }

    private void MergeNonResidentAttribute(RawNonResidentAttributeHeader header, AttributeListAttribute.Entry entry, ReadOnlySpan<byte> dataruns)
    {
        if (IsAttributePresent(header, entry))
            return;

        var extent = RunHelpers.GetExtentOfNrh(header, dataruns);

        var matchingAttribute = GetMatchingAttribute(header, entry);
        if (matchingAttribute != null)
        {
            var stream = matchingAttribute.Accessor as NonResidentDataStream;
            if (stream == null)
                throw new InvalidOperationException(
                    $"Found an existing, corresponding attribute to the one attempted to parse"
                    + $" (Type={matchingAttribute.Type}, Name={matchingAttribute.Name}, <=> Type={header.Common.Type}, Name={entry.Name})"
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
                Name = entry.Name,

                Accessor = new NonResidentDataStream(_volume.GetUnderlyingStream(), _volume.BootSector.BytesPerCluster),
                BytesPerCluster = _volume.BootSector.BytesPerCluster
            };

            ((NonResidentDataStream)attribute.Accessor).AddExtent(extent);
            _file.NtfsAttributes.Add(attribute);
        }
    }

    private bool IsAttributePresent(RawResidentAttributeHeader header, AttributeListAttribute.Entry entry)
    { 
        return _file.NtfsAttributes
            .Where(x => x.Source.Id == header.Common.AttributeId)
            .Where(x => x.Source.BaseReference == entry.BaseFileReference)
            .FirstOrDefault() != null;
    }

    private bool IsAttributePresent(RawNonResidentAttributeHeader header, AttributeListAttribute.Entry entry)
    {
        return _file.NtfsAttributes
            .Where(x => x.Source.Id == header.Common.AttributeId)
            .Where(x => x.Source.BaseReference == entry.BaseFileReference)
            .FirstOrDefault() != null;
        //.Where(x => ((NonResidentDataStream)x.Accessor).Extents.Any(x => x.LowestVcn == header.StartingVcn))
        //.FirstOrDefault(x => x.Name == entry.Name) != null;
    }

    private NtfsAttribute? GetMatchingAttribute(RawNonResidentAttributeHeader header, AttributeListAttribute.Entry entry)
    {
        return _file.NtfsAttributes
            .Where(x => x.Type == header.Common.Type)
            .Where(x => x.Source.BaseReference == entry.BaseFileReference)
            .FirstOrDefault();
    }
}
