using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Nternal.Core;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Generic;
using Nternal.Ntfs.Helpers;
using Nternal.Ntfs.Internal;
using Nternal.Ntfs.Streams;

namespace Nternal.Ntfs;

public class NtfsFile
{
    public readonly List<NtfsAttribute> NtfsAttributes = [];

    public readonly RawFileRecord RecordHeader;
    private readonly NtfsVolume _ntfsVolume;
    private readonly ulong _recordNumber;

    public NtfsFile(RawFileRecord record, IDataStream underlying, NtfsVolume ntfsVolume, ulong recordNumber,
        bool postXP = true)
    {
        _ntfsVolume = ntfsVolume;
        _recordNumber = recordNumber;
        RecordHeader = record;

        ReadOnlySpan<byte> attributeBuffer = record.AttributeData;

        while (true)
        {
            if (attributeBuffer.Length < 4)
                throw new InvalidDataException(
                    $"File record {recordNumber} ends without an attribute end marker (0xFFFFFFFF)!");

            var type = (NtfsAttributeType)BinaryPrimitives.ReadUInt32LittleEndian(attributeBuffer);
            if (type == NtfsAttributeType.EndMarker)
                break;

            // Smallest possible attribute header: the common part (16 bytes)
            if (attributeBuffer.Length < Unsafe.SizeOf<RawAttributeHeader>())
                throw new InvalidDataException(
                    $"File record {recordNumber}: {attributeBuffer.Length} bytes remain, which is too few for an attribute header!");

            var header = MemoryMarshal.Cast<byte, RawAttributeHeader>(attributeBuffer)[0];
            if (header.Length == 0)
                throw new InvalidDataException(
                    "Encountered an attribute header with a length of zero - this may never happen on a healthy volume!");

            ValidateAttributeBounds(attributeBuffer, header, recordNumber);

            var name =
                Encoding.Unicode.GetString(attributeBuffer.Slice(header.NameOffset, header.NameLength * 2));

            if (header.NonResidentFlag == 0)
            {
                var residentHeader =
                    MemoryMarshal.Cast<byte, RawResidentAttributeHeader>(attributeBuffer)[0];
                var data = attributeBuffer.Slice(residentHeader.AttributeOffset,
                    (int)residentHeader.AttributeLength);
                ParseResidentAttribute(residentHeader, name, data);
            }

            else
            {
                var nonResidentHeader =
                    MemoryMarshal.Cast<byte, RawNonResidentAttributeHeader>(attributeBuffer)[0];
                var dataRuns = attributeBuffer.Slice(nonResidentHeader.DataRunsOffset,
                    (int)(header.Length - nonResidentHeader.DataRunsOffset));
                ParseNonResidentAttribute(nonResidentHeader, name, dataRuns, underlying);
            }

            attributeBuffer = attributeBuffer.Slice((int)header.Length);
        }

        ProcessAttributeListIfPresent();
    }

    /// <summary>
    ///     Validates that an attribute header's self-described lengths and offsets stay within the
    ///     attribute itself and within the remaining record, so corrupt input produces a descriptive
    ///     <see cref="InvalidDataException" /> instead of an unrelated slicing exception.
    /// </summary>
    private static void ValidateAttributeBounds(ReadOnlySpan<byte> attributeBuffer, RawAttributeHeader header,
        ulong recordNumber)
    {
        var ctx = $"Record {recordNumber}, attribute {header.Type} (id {header.AttributeId})";

        if (header.Length < Unsafe.SizeOf<RawAttributeHeader>() || header.Length > attributeBuffer.Length)
            throw new InvalidDataException(
                $"{ctx}: length {header.Length} is invalid ({attributeBuffer.Length} bytes remain in the record).");

        if (header.NameLength > 0 && header.NameOffset + header.NameLength * 2L > header.Length)
            throw new InvalidDataException(
                $"{ctx}: name (offset {header.NameOffset}, {header.NameLength} chars) exceeds the attribute.");

        if (header.NonResidentFlag == 0)
        {
            if (header.Length < Unsafe.SizeOf<RawResidentAttributeHeader>())
                throw new InvalidDataException($"{ctx}: too short ({header.Length}) for a resident header.");

            var resident = MemoryMarshal.Cast<byte, RawResidentAttributeHeader>(attributeBuffer)[0];
            if (resident.AttributeOffset + (long)resident.AttributeLength > header.Length)
                throw new InvalidDataException(
                    $"{ctx}: resident data (offset {resident.AttributeOffset}, length {resident.AttributeLength}) exceeds the attribute.");
        }
        else
        {
            if (header.Length < Unsafe.SizeOf<RawNonResidentAttributeHeader>())
                throw new InvalidDataException($"{ctx}: too short ({header.Length}) for a non-resident header.");

            var nonResident = MemoryMarshal.Cast<byte, RawNonResidentAttributeHeader>(attributeBuffer)[0];
            if (nonResident.DataRunsOffset > header.Length)
                throw new InvalidDataException(
                    $"{ctx}: data runs offset {nonResident.DataRunsOffset} exceeds the attribute.");
        }
    }

    public FileReference GetReference()
    {
        return new FileReference(_recordNumber, RecordHeader.SequenceNumber);
    }

    public T? GetAttributeData<T>(NtfsAttributeType type, string name) where T : IParsableAttribute, new()
    {
        var attribute = NtfsAttributes
            .Where(x => x.Name == name)
            .FirstOrDefault(x => x.Type == type);

        return attribute == null ? default : attribute.Cast<T>();
    }

    public T GetRequiredAttributeData<T>(NtfsAttributeType type, string name) where T : IParsableAttribute, new()
    {
        return GetAttributeData<T>(type, name) ??
               throw new KeyNotFoundException(
                   $"Could not find attribute \"{name}\" of type {type} in record {GetReference()}!");
    }

    public IDataStream GetDataStream(string name = "")
    {
        var attribute = NtfsAttributes
            .Where(x => x.Type == NtfsAttributeType.Data)
            .FirstOrDefault(x => x.Name == name);

        if (attribute == null)
            throw new FileNotFoundException(
                $"Could not find {(name.Length == 0 ? "unnamed data-stream" : $"data-stream named \"{name}\"")} within file!");

        return attribute.Cast<DataAttribute>().GetStream();
    }

    public T? GetAttributeData<T>(string name = "") where T : IParsableAttribute, new()
    {
        var attribute = NtfsAttributes
            .Where(x => x.Name == name)
            .FirstOrDefault(x => x.Type == T.AttributeType);

        return attribute == null ? default : attribute.Cast<T>();
    }

    public T[] GetAttributeDataArray<T>(string name = "") where T : IParsableAttribute, new()
    {
        return NtfsAttributes
            .Where(x => x.Name == name)
            .Where(x => x.Type == T.AttributeType)
            .Select(x => x.Cast<T>())
            .ToArray();
    }

    public FileNameData? GetBestFittingName()
    {
        var names = GetAttributeDataArray<FileNameAttribute>().Select(x => x.FileNameData).ToList();

        if (!NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.Disable8dot3LegacyMode))
            return names.FirstOrDefault(n => n.FileNamespace.HasFlag(FileNameNamespace.Dos));

        // Prefer the long Win32 name over the 8.3 DOS alias.
        return names.FirstOrDefault(n => n.FileNamespace.HasFlag(FileNameNamespace.Win32))
               ?? names.FirstOrDefault(n => n.FileNamespace == FileNameNamespace.Posix)
               ?? names.FirstOrDefault();
    }

    private void ParseResidentAttribute(RawResidentAttributeHeader header, string name, ReadOnlySpan<byte> data)
    {
        NtfsAttributes.Add(new NtfsAttribute
        {
            Type = header.Common.Type,
            IsNonResident = false,
            Flags = header.Common.Flags,
            ResidentSource = new AttributeSource(GetReference(), header.Common.AttributeId),
            Name = name,

            Accessor = new ResidentDataStream(data.ToArray()),
            BytesPerCluster = _ntfsVolume.BootSector.BytesPerCluster
        });
    }

    private void ParseNonResidentAttribute(RawNonResidentAttributeHeader header, string name,
        ReadOnlySpan<byte> dataruns, IDataStream underlying)
    {
        var extent = RunHelpers.GetExtentOfNrh(header, dataruns);
        extent.Source = new AttributeSource(GetReference(), header.Common.AttributeId);

        var matchingAttribute = GetMatchingAttribute(header, name);
        if (matchingAttribute != null)
        {
            var stream = matchingAttribute.Accessor as NonResidentDataStream;
            if (stream == null)
                throw new InvalidOperationException(
                    $"Found an existing, corresponding attribute to the one attempted to parse"
                    + $" (Type={matchingAttribute.Type}, Name={matchingAttribute.Name}, <=> Type={header.Common.Type}, Name={name})"
                    + $", but the found accessor is not a NonResidentDataStream, but of type {matchingAttribute.Accessor.GetType()} - this should never happen on a healthy volume!");

            stream.AddExtent(extent);
        }
        else
        {
            var attribute = new NtfsAttribute
            {
                Type = header.Common.Type,
                IsNonResident = true,
                Flags = header.Common.Flags,
                Name = name,

                Accessor = new NonResidentDataStream(underlying, _ntfsVolume.BootSector.BytesPerCluster),
                BytesPerCluster = _ntfsVolume.BootSector.BytesPerCluster
            };

            ((NonResidentDataStream)attribute.Accessor).AddExtent(extent);
            NtfsAttributes.Add(attribute);
        }
    }

    private NtfsAttribute? GetMatchingAttribute(RawNonResidentAttributeHeader header, string name)
    {
        return NtfsAttributes.FirstOrDefault(x => x.IsNonResident && x.Type == header.Common.Type && x.Name == name);
    }

    private void ProcessAttributeListIfPresent()
    {
        var attributeList = NtfsAttributes.FirstOrDefault(x => x.Type == NtfsAttributeType.AttributeList);
        if (attributeList == null)
            return;

        var data = attributeList.Cast<AttributeListAttribute>();
        FileRecordMerger merger = new(_ntfsVolume, this, _ntfsVolume.MftFile == null);
        foreach (var entry in data.Entries)
            merger.MergeListedEntry(entry);

        var produced = NtfsAttributes.Sum(a =>
            a.IsNonResident ? ((NonResidentDataStream)a.Accessor).Extents.Count : 1);
        var expected = data.Entries.Count + 1;

        // Be aware, $AttributeList does not list itself!
        // Mismatches are fatal in paranoid mode only; otherwise we keep whatever merged successfully.
        if (produced != expected && NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.ParanoidParsing))
        {
#if DEBUG
            StringBuilder _produced = new();
            foreach (var entry in NtfsAttributes)
                if (!entry.IsNonResident)
                    Console.WriteLine($"Present - Resident - {entry.Type}, {entry.Source.Id}, {entry.Name}");
            foreach (var entry in data.Entries)
                Console.WriteLine($"Listed - Resident - {entry.Type}, {entry.Id}, {entry.Name}");
#endif

            throw new InvalidDataException($"Merge resulted in {produced} attribute entries (seperating extents), " +
                                           $"but it should be {expected}!");
        }

        foreach (var group in NtfsAttributes.Where(a => a.IsNonResident).GroupBy(a => (a.Type, a.Name)))
        {
            if (group.Count() > 1)
                throw new InvalidDataException($"Attribute {group.Key} was split into {group.Count()} parts!");

            var extents = ((NonResidentDataStream)group.Single().Accessor).Extents;
            extents.Sort((a, b) => a.LowestVcn.CompareTo(b.LowestVcn));

            if (extents[0].LowestVcn != 0)
                throw new InvalidDataException($"Attribute {group.Key} has no extent at VCN 0!");
            for (var i = 1; i < extents.Count; i++)
                if (extents[i].LowestVcn != extents[i - 1].HighestVcn + 1)
                    throw new InvalidDataException($"Attribute {group.Key} has a gap or overlap at extent {i}!");
        }
    }
}