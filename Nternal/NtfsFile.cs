using Nternal.Core.Streams;
using Nternal.Helpers;
using Nternal.Internal;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Helpers;
using Nternal.Streams;
using Nternal.Structs;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static System.Net.WebRequestMethods;

namespace Nternal.Attributes
{
    public class NtfsFile
    {
        private readonly NtfsVolume _ntfsVolume;
        private readonly ulong _recordNumber;

        public readonly RawFileRecord RecordHeader;
        public readonly List<NtfsAttribute> NtfsAttributes = [];

        public FileReference GetReference() =>
            new FileReference(_recordNumber, RecordHeader.SequenceNumber);

        public NtfsFile(RawFileRecord record, IDataStream underlying, NtfsVolume ntfsVolume, ulong recordNumber, bool postXP = true)
        {
            _ntfsVolume = ntfsVolume;
            _recordNumber = recordNumber;
            RecordHeader = record;

            ReadOnlySpan<byte> attributeBuffer = record.AttributeData;

            while (true)
            {
                var type = (NtfsAttributeType)BinaryPrimitives.ReadUInt32LittleEndian(attributeBuffer);
                if (type == NtfsAttributeType.EndMarker)
                    break;

                RawAttributeHeader header = MemoryMarshal.Cast<byte, RawAttributeHeader>(attributeBuffer)[0];
                if (header.Length == 0)
                    throw new InvalidDataException($"Encountered an attribute header with a length of zero - this may never happen on a healthy volume!");

                string name =
                    Encoding.Unicode.GetString(attributeBuffer.Slice(header.NameOffset, header.NameLength * 2));

                if (header.NonResidentFlag == 0)
                {
                    RawResidentAttributeHeader residentHeader =
                        MemoryMarshal.Cast<byte, RawResidentAttributeHeader>(attributeBuffer)[0];
                    ReadOnlySpan<byte> data = attributeBuffer.Slice(residentHeader.AttributeOffset,
                        (int)residentHeader.AttributeLength);
                    ParseResidentAttribute(residentHeader, name, data);
                }

                else
                {
                    RawNonResidentAttributeHeader nonResidentHeader =
                        MemoryMarshal.Cast<byte, RawNonResidentAttributeHeader>(attributeBuffer)[0];
                    ReadOnlySpan<byte> dataRuns = attributeBuffer.Slice(nonResidentHeader.DataRunsOffset,
                        (int)(header.Length - nonResidentHeader.DataRunsOffset));
                    ParseNonResidentAttribute(nonResidentHeader, name, dataRuns, underlying);
                }

                attributeBuffer = attributeBuffer.Slice((int)header.Length);
            }

            ProcessAttributeListIfPresent();
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

            // Prefer the long Win32 name over the 8.3 DOS alias.
            return names.FirstOrDefault(n => n.FileNamespace.HasFlag(FileNameNamespace.Win32))
                   ?? names.FirstOrDefault(n => n.FileNamespace == FileNameNamespace.Posix)
                   ?? names.FirstOrDefault();
        }

        private void ParseResidentAttribute(RawResidentAttributeHeader header, string name, ReadOnlySpan<byte> data)
        {
            NtfsAttributes.Add(new NtfsAttribute()
            {
                Type = header.Common.Type,
                IsNonResident = false,
                Flags = header.Common.Flags,
                ResidentSource = new(GetReference(), header.Common.AttributeId),
                Name = name,

                Accessor = new ResidentDataStream(data.ToArray()),
                BytesPerCluster = _ntfsVolume.BootSector.BytesPerCluster
            });
        }

        private void ParseNonResidentAttribute(RawNonResidentAttributeHeader header, string name,
            ReadOnlySpan<byte> dataruns, IDataStream underlying)
        {
            var extent = RunHelpers.GetExtentOfNrh(header, dataruns);
            extent.Source = new(GetReference(), header.Common.AttributeId);

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
                    Name = name,

                    Accessor = new NonResidentDataStream(underlying, _ntfsVolume.BootSector.BytesPerCluster),
                    BytesPerCluster = _ntfsVolume.BootSector.BytesPerCluster
                };

                ((NonResidentDataStream)attribute.Accessor).AddExtent(extent);
                NtfsAttributes.Add(attribute);
            }
        }

        private NtfsAttribute? GetMatchingAttribute(RawNonResidentAttributeHeader header, string name) =>
            NtfsAttributes.FirstOrDefault(x => x.IsNonResident && x.Type == header.Common.Type && x.Name == name);

        private void ProcessAttributeListIfPresent()
        {
            var attributeList = NtfsAttributes.FirstOrDefault(x => x.Type == NtfsAttributeType.AttributeList);
            if (attributeList == null)
                return;

            var data = attributeList.Cast<AttributeListAttribute>();
            FileRecordMerger merger = new(_ntfsVolume, this, _ntfsVolume.MftFile == null);
            foreach (var entry in data.Entries)
                merger.MergeListedEntry(entry);

            int produced = NtfsAttributes.Sum(a =>
                a.IsNonResident ? ((NonResidentDataStream)a.Accessor).Extents.Count : 1);
            int expected = data.Entries.Count + 1;

            // Be aware, $AttributeList does not list itself!
            if (produced != expected)
            {
#if DEBUG
                StringBuilder _produced = new();
                foreach (var entry in NtfsAttributes)
                    if (!entry.IsNonResident)
                        Console.WriteLine($"Present - Resident - {entry.Type}, {entry.Source.Id}, {entry.Name}");
                foreach (var entry in data.Entries)
                    Console.WriteLine($"Listed - Resident - {entry.Type}, {entry.Id}, {entry.Name}");
#endif

                throw new Exception($"Merge resulted in {produced} attribute entries (seperating extents), " +
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
                for (int i = 1; i < extents.Count; i++)
                    if (extents[i].LowestVcn != extents[i - 1].HighestVcn + 1)
                        throw new InvalidDataException($"Attribute {group.Key} has a gap or overlap at extent {i}!");
            }
        }
    }
}