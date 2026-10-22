using Nternal.Core;
using Nternal.Core.Extensions;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Helpers;

namespace Nternal.Ntfs.Index;

public class IndexParser
{
    private const int IndexAllocationVcnUnit = 512;
    internal readonly BitmapAttribute? Bitmap;

    private readonly NtfsBootSector BootSector;
    internal readonly CollationHandler CollationHandler;
    internal readonly IndexAllocationAttribute? IndexAllocation;
    internal readonly IndexRootAttribute IndexRoot;

    internal readonly bool Paranoid;
    private readonly IDataStream? UnalignedIndexAllocationStream;

    public IndexParser(NtfsFile file, string index, CollationHandler handler, NtfsBootSector bootSector)
    {
        IndexRoot = file.GetAttributeData<IndexRootAttribute>(NtfsAttributeType.IndexRoot, index) ??
                    throw new ArgumentException($"Index \"{index}\" could not be found in file {file.GetReference()}!");
        IndexAllocation = file.GetAttributeData<IndexAllocationAttribute>(NtfsAttributeType.IndexAllocation, index);
        Bitmap = file.GetAttributeData<BitmapAttribute>(NtfsAttributeType.Bitmap, index);

        Paranoid = NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.ParanoidParsing);
        CollationHandler = handler;
        BootSector = bootSector;

        if (IndexAllocation != null)
            UnalignedIndexAllocationStream = new UnalignedDataStream(IndexAllocation.GetStream());
    }

    public IndexParser(IndexRootAttribute root, IndexAllocationAttribute? allocation, BitmapAttribute? bitmap,
        CollationHandler handler, NtfsBootSector bootSector)
    {
        IndexRoot = root;
        IndexAllocation = allocation;
        Bitmap = bitmap;
        Paranoid = NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.ParanoidParsing);
        CollationHandler = handler;
        BootSector = bootSector;

        if (IndexAllocation != null)
            UnalignedIndexAllocationStream = new UnalignedDataStream(IndexAllocation.GetStream());
    }

    public IndexEntry? TryFindRootEntry(ReadOnlySpan<byte> key)
    {
        // We start inside the index root :)
        if (Paranoid && IndexRoot.Underlying.IsNonResident)
            throw new InvalidDataException(
                "Expected $Index_Root to be resident, but came out as non-resident - this should never be the case!");

        if (!IndexRoot.Underlying.Accessor.TryGetMemory(out var buffer))
        {
            buffer = new byte[IndexRoot.Underlying.Accessor.Length];
            IndexRoot.Underlying.Accessor.ReadExactly(0, buffer);
        }

        var offset = 16 + IndexRoot.OffsetToFirstEntry;
        ReadOnlySpan<byte> bufferData = buffer[offset..];
        return LookInIndexTree(bufferData, key);
    }

    public IEnumerable<IndexEntry> EnumerateEntries()
    {
        if (!IndexRoot.Underlying.Accessor.TryGetMemory(out var buffer))
        {
            buffer = new byte[IndexRoot.Underlying.Accessor.Length];
            IndexRoot.Underlying.Accessor.ReadExactly(0, buffer);
        }

        var offset = 16 + IndexRoot.OffsetToFirstEntry;
        foreach (var indexEntry in TraverseIndexBuffer(buffer[offset..].ToArray()))
            yield return indexEntry;
    }

    private IEnumerable<IndexEntry> TraverseIndexBuffer(ReadOnlyMemory<byte> _bufferData)
    {
        var bufferData = _bufferData;

        while (!bufferData.IsEmpty)
        {
            // Parse & look for it
            var length = bufferData.Span.ReadUInt16(8);
            var entry = new IndexEntry(bufferData[..length], IndexRoot, CollationHandler);

            // we arrived at the end of the index-root!
            if (entry.Flags.HasFlag(IndexEntry.IndexFlags.HasSubnode))
                foreach (var indexEntry in TraverseIndexBuffer(GetIndexBuffer(entry.SubnodeVcn!.Value)))
                    yield return indexEntry;

            if (entry.Flags.HasFlag(IndexEntry.IndexFlags.IsLast))
                yield break;

            // For now, for $I30: Only return names with the Win32 namespace
            if (IndexRoot.IndexedType == NtfsAttributeType.FileName)
            {
                var nameSpace = (FileNameNamespace)entry.GetKey()[65];

                if (NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.Disable8dot3LegacyMode))
                {
                    if (nameSpace.HasFlag(FileNameNamespace.Win32))
                        yield return entry;

                    if (NtfsPolicyConfig.ListPosixFileNames && nameSpace == FileNameNamespace.Posix)
                        yield return entry;
                }

                else
                {
                    if (nameSpace.HasFlag(FileNameNamespace.Dos))
                        yield return entry;
                }
            }
            else
            {
                yield return entry;
            }

            // Look for next item in root
            bufferData = bufferData.Slice(length);
        }
    }

    private byte[]? GetIndexBuffer(long node)
    {
        if (Bitmap == null || UnalignedIndexAllocationStream == null)
            return null;

        var byteOffset = node * (IndexRoot.ChildVcnInSectorUnits ? IndexAllocationVcnUnit : BootSector.BytesPerCluster);
        var bitmapBit = byteOffset / IndexRoot.BytesPerIndexBlock;

        if (!Bitmap.IsSet(bitmapBit))
            if (Paranoid) throw new Exception("A free sub-node was specified within an index entry!");
            else return null;

        Span<byte> indexBuffer = new byte[IndexRoot.BytesPerIndexBlock];
        UnalignedIndexAllocationStream.ReadExactly(byteOffset, indexBuffer);

        string magicNumber;
        if ((magicNumber = indexBuffer.ReadAsciiString(0, 4)) != IndexAllocationAttribute.ExpectedMagicNumber)
            throw new InvalidDataException($"Index-Buffer {node} has an invalid magic number ({magicNumber}, " +
                                           $"expected \"{IndexAllocationAttribute.ExpectedMagicNumber}\")!");

        FixupHelper.FixupRecord(indexBuffer);

        var first = 0x18 + (int)indexBuffer.ReadUInt32(0x18);
        var end = 0x18 + (int)indexBuffer.ReadUInt32(0x1C);
        if (first < 0x28 || end > indexBuffer.Length || first > end)
            throw new InvalidDataException($"Index buffer {node} has an invalid entry range");

        return indexBuffer[first..end].ToArray();
    }

    private IndexEntry? TryFindAllocationEntry(long node, ReadOnlySpan<byte> key)
    {
        // We do not have a valid index-allocation => null
        var buffer = GetIndexBuffer(node);
        if (buffer == null)
            return null; // or throw, if Paranoid should have already caught this upstream

        return LookInIndexTree(buffer, key);
    }

    private IndexEntry? LookInIndexTree(ReadOnlySpan<byte> bufferData, ReadOnlySpan<byte> key)
    {
        while (!bufferData.IsEmpty)
        {
            // Parse & look for it
            var length = bufferData.ReadUInt16(8);
            var entry = new IndexEntry(bufferData[..length].ToArray(), IndexRoot, CollationHandler);

            // we arrived at the end of the index-root!
            if (entry.Flags.HasFlag(IndexEntry.IndexFlags.IsLast))
            {
                if (entry.Flags.HasFlag(IndexEntry.IndexFlags.HasSubnode))
                    return TryFindAllocationEntry(entry.SubnodeVcn!.Value, key);
                break;
            }

            var result = CollationHandler.CompareToTarget(entry.RawData, key, IndexRoot.CollationRule);

            // They match => return
            if (result == 0)
                return entry;

            // Is it within a sub-node-vcn? 
            if (result < 0)
                // Yes - but do we have a subnode-vcn?
                return entry.SubnodeVcn != null ? TryFindAllocationEntry(entry.SubnodeVcn.Value, key) : null;

            // Look for next item in root
            bufferData = bufferData.Slice(length);
        }

        return null;
    }
}