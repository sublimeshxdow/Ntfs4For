using System.Diagnostics;
using System.Text;
using Nternal.Attributes;
using Nternal.Extensions;
using Nternal.Helpers;
using Nternal.Ntfs.Attributes;
using Nternal.Structs;

namespace Nternal.Ntfs.Index;

public class IndexParser
{
    internal readonly IndexRootAttribute IndexRoot;
    internal readonly IndexAllocationAttribute? IndexAllocation;
    internal readonly BitmapAttribute? Bitmap;

    internal readonly bool Paranoid;

    internal readonly CollationHandler CollationHandler;
    internal readonly FixupHelper FixupHelper;

    public IndexParser(NtfsFile file, string index, CollationHandler handler, FixupHelper helper)
    {
        IndexRoot = file.GetAttributeData<IndexRootAttribute>(NtfsAttributeType.IndexRoot, index) ??
                    throw new ArgumentException($"Index \"{index}\" could not be found in file {file.GetReference()}!");
        IndexAllocation = file.GetAttributeData<IndexAllocationAttribute>(NtfsAttributeType.IndexAllocation, index);
        Bitmap = file.GetAttributeData<BitmapAttribute>(NtfsAttributeType.Bitmap, index);

        Paranoid = NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.ParanoidParsing);
        CollationHandler = handler;
        FixupHelper = helper;
    }

    public IndexParser(IndexRootAttribute root, IndexAllocationAttribute? allocation, BitmapAttribute? bitmap,
        CollationHandler handler, FixupHelper helper)
    {
        IndexRoot = root;
        IndexAllocation = allocation;
        Bitmap = bitmap;
        Paranoid = NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.ParanoidParsing);
        CollationHandler = handler;
        FixupHelper = helper;
    }

    public IndexEntry? TryFindRootEntry(ReadOnlySpan<byte> key)
    {
        // We start inside the index root :)
        if (Paranoid && IndexRoot.Underlying.IsNonResident)
            throw new InvalidDataException(
                $"Expected $Index_Root to be resident, but came out as non-resident - this should never be the case!");

        if (!IndexRoot.Underlying.Accessor.TryGetMemory(out Span<byte> buffer))
        {
            buffer = new byte[IndexRoot.Underlying.Accessor.Length];
            IndexRoot.Underlying.Accessor.ReadExactly(0, buffer);
        }

        int offset = 16 + IndexRoot.OffsetToFirstEntry;
        ReadOnlySpan<byte> bufferData = buffer[offset..buffer.Length];
        return LookInIndexTree(bufferData, key);
    }

    public IEnumerable<IndexEntry> EnumerateEntries()
    {
        if (!IndexRoot.Underlying.Accessor.TryGetMemory(out Span<byte> buffer))
        {
            buffer = new byte[IndexRoot.Underlying.Accessor.Length];
            IndexRoot.Underlying.Accessor.ReadExactly(0, buffer);
        }

        int offset = 16 + IndexRoot.OffsetToFirstEntry;
        foreach (var indexEntry in TraverseIndexBuffer(buffer[offset..buffer.Length].ToArray()))
            yield return indexEntry;
    }

    private IEnumerable<IndexEntry> TraverseIndexBuffer(ReadOnlyMemory<byte> _bufferData)
    {
        var bufferData = _bufferData;

        while (!bufferData.IsEmpty)
        {
            // Parse & look for it
            ushort length = bufferData.Span.ReadUInt16(8);
            var entry = new IndexEntry(bufferData[0..length], IndexRoot, CollationHandler);

            // we arrived at the end of the index-root!
            if (entry.Flags.HasFlag(IndexEntry.IndexFlags.HasSubnode))
            {
                foreach (var indexEntry in TraverseIndexBuffer(GetIndexBuffer(entry.SubnodeVcn!.Value)))
                    yield return indexEntry;
            }

            if (entry.Flags.HasFlag(IndexEntry.IndexFlags.IsLast))
                yield break;

            // TODO - For now, for $I30: Only return names with the Win32 namespace
            if (IndexRoot.IndexedType == NtfsAttributeType.FileName)
            {
                var nameSpace = (FileNameNamespace)entry.GetKey()[65];
                if (nameSpace.HasFlag(FileNameNamespace.Win32))
                    yield return entry;

                if (NtfsPolicyConfig.ListPosixFileNames && nameSpace == FileNameNamespace.Posix)
                    yield return entry;
            }

            // Look for next item in root
            bufferData = bufferData.Slice(length);
        }
    }

    private byte[]? GetIndexBuffer(long node)
    {
        if (IndexAllocation == null || Bitmap == null)
            return null;

        if (!Bitmap.IsSet(node))
            if (Paranoid) throw new Exception("A free sub-node was specified within an index entry!");
            else return null;

        byte[] indexBuffer = new byte[IndexRoot.BytesPerIndexBlock];
        IndexAllocation.GetStream().ReadExactly(node * indexBuffer.Length, indexBuffer);
        FixupHelper.FixupRecord(indexBuffer);

        int offset = 0x18 + indexBuffer.AsSpan().ReadUInt16(0x18);
        return indexBuffer.AsSpan(offset).ToArray();
    }

    private IndexEntry? TryFindAllocationEntry(long node, ReadOnlySpan<byte> key)
    {
        // We do not have a valid index-allocation => null
        if (IndexAllocation == null || Bitmap == null)
            return null;

        var fullBuffer = GetIndexBuffer(node);
        int offset = 0x18 + fullBuffer.AsSpan().ReadUInt16(0x18);
        return LookInIndexTree(fullBuffer, key);
    }

    private IndexEntry? LookInIndexTree(ReadOnlySpan<byte> bufferData, ReadOnlySpan<byte> key)
    {
        while (!bufferData.IsEmpty)
        {
            // Parse & look for it
            ushort length = bufferData.ReadUInt16(8);
            var entry = new IndexEntry(bufferData[0..length].ToArray(), IndexRoot, CollationHandler);

            // we arrived at the end of the index-root!
            if (entry.Flags.HasFlag(IndexEntry.IndexFlags.IsLast))
            {
                if (entry.Flags.HasFlag(IndexEntry.IndexFlags.HasSubnode))
                    return TryFindAllocationEntry(entry.SubnodeVcn!.Value, key);
                else
                    break;
            }

            int result = CollationHandler.CompareToTarget(entry.RawData, key, IndexRoot.CollationRule);

            // They match => return
            if (result == 0)
                return entry;

            // Is it within a sub-node-vcn? 
            else if (result < 0)
                // Yes - but do we have a subnode-vcn?
                return entry.SubnodeVcn != null ? TryFindAllocationEntry(entry.SubnodeVcn.Value, key) : null;

            // Look for next item in root
            bufferData = bufferData.Slice(length);
        }

        return null;
    }
}