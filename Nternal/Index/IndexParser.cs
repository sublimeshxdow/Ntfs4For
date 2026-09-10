using Nternal.Extensions;
using Nternal.Helpers;
using Nternal.Ntfs.Attributes;
using Nternal.Structs;

namespace Nternal.Ntfs.Index;

public class IndexParser
{
    private readonly IndexRootAttribute _indexRoot;
    private readonly IndexAllocationAttribute? _indexAllocation;
    private readonly BitmapAttribute? _bitmap;
    
    private readonly bool _paranoid;
    private readonly FixupHelper _fixupHelper;
    
    public IndexParser(IndexRootAttribute root, IndexAllocationAttribute? allocation, BitmapAttribute? bitmap, bool paranoid, FixupHelper helper)
    {
        _indexRoot = root;
        _indexAllocation = allocation;
        _bitmap = bitmap;
        _paranoid = paranoid;
        _fixupHelper = helper;
    }

    public IndexEntry? TryFindRootEntry(ReadOnlySpan<byte> key)
    {
        // We start inside the index root :)
        if (_paranoid && _indexRoot.Underlying.IsNonResident)
            throw new InvalidDataException(
                $"Expected $Index_Root to be resident, but came out as non-resident - this should never be the case!");

        if (!_indexRoot.Underlying.Accessor.TryGetMemory(out Span<byte> buffer))
        {
            buffer = new byte[_indexRoot.Underlying.Accessor.Length];
            _indexRoot.Underlying.Accessor.ReadExactly(0, buffer);
        }

        int offset = 16 + _indexRoot.OffsetToFirstEntry;
        ReadOnlySpan<byte> bufferData = buffer[offset..buffer.Length];
        return TraverseIndexTree(bufferData, key);
    }

    private IndexEntry? TryFindAllocationEntry(long node, ReadOnlySpan<byte> key)
    {
        // We do not have a valid index-allocation => null
        if (_indexAllocation == null || _bitmap == null)
            return null;

        if (_bitmap.IsSet(node))
            if (_paranoid)
                throw new Exception($"A free sub-node was specified within an index entry!");
            else
                return null;

        Span<byte> indexBuffer = new byte[_indexRoot.BytesPerIndexBlock];
        _indexAllocation.GetStream().ReadExactly(node * indexBuffer.Length, indexBuffer);
        _fixupHelper.FixupRecord(indexBuffer);
        return TraverseIndexTree(indexBuffer, key);
    }

    private IndexEntry? TraverseIndexTree(ReadOnlySpan<byte> bufferData, ReadOnlySpan<byte> key)
    {
        while (!bufferData.IsEmpty)
        {
            // Parse & look for it
            ushort length = bufferData.ReadUInt16(8);
            var entry = new IndexEntry(bufferData[0..length].ToArray(), _indexRoot);

            // we arrived at the end of the index-root!
            if (entry.Flags.HasFlag(IndexEntry.IndexFlags.IsLast))
            {
                if (entry.Flags.HasFlag(IndexEntry.IndexFlags.HasSubnode))
                    return TryFindAllocationEntry(entry.SubnodeVcn!.Value, key);
                else
                    break;
            }

            int result = ComparisonUtils.CompareToTarget(entry.RawData, key, _indexRoot.CollationRule);

            // They match => return
            if (result == 0)
                return entry;

            // Is it within a sub-node-vcn? 
            else if (result == -1)
            {
                // Yes - but do we have a subnode-vcn?
                if (entry.SubnodeVcn != null)
                    return TryFindAllocationEntry(entry.SubnodeVcn.Value, key);

                // No => it cannot exist!
                else
                    return null;
            }

            // Look for next item in root
            bufferData = bufferData.Slice(length);
        }

        return null;
    }
}