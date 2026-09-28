using System.Buffers;
using System.Reflection.PortableExecutable;

namespace Nternal.Streams;

public partial class NonResidentDataStream
{
    public long ReadIntoBuffer(Span<byte> buffer, long startingVcn, int count, bool exactly)
    {
        long lowVcn = startingVcn, highVcn = startingVcn + count;
        long clustersCovered = 0;

        foreach (var extent in Extents)
        {
            long extentEndExclusive = extent.HighestVcn + 1;

            if (extentEndExclusive > lowVcn && extent.LowestVcn < highVcn)
            {
                long overlapStart = Math.Max(extent.LowestVcn, lowVcn);
                long overlapEnd = Math.Min(extentEndExclusive, highVcn);
                long overlapClusters = overlapEnd - overlapStart;

                long bufferByteOffset = (overlapStart - lowVcn) * _bytesPerCluster;
                int byteLength = (int)(overlapClusters * _bytesPerCluster);

                Span<byte> subBuffer = buffer.Slice((int)bufferByteOffset, byteLength);
                ReadFromExtend(extent, subBuffer, overlapStart, (int)overlapClusters);

                clustersCovered += overlapClusters;
            }

            if (extentEndExclusive >= highVcn)
                break;
        }

        if (exactly && clustersCovered != count)
            throw new InvalidDataException(
                $"Extents have a gap: expected to cover {count} clusters but only found {clustersCovered}.");

        return clustersCovered;
    }

    public void ReadFromExtend(Extent extent, Span<byte> bufferToFill, long startingVcn, int count)
    {
        long firstVcn = startingVcn, lastVcn = startingVcn + count;
        if (firstVcn < extent.LowestVcn || lastVcn > extent.HighestVcn + 1)
            throw new ArgumentOutOfRangeException(nameof(startingVcn),
                $"Attempted to read clusters {startingVcn}-{lastVcn}, but the extent only covers {extent.LowestVcn}-{extent.HighestVcn}!");

        long relFirstVcn = firstVcn - extent.LowestVcn, relLastVcn = lastVcn - extent.LowestVcn;

        long runStartVcn = 0; // extent-relative VCN where the CURRENT run begins
        long clustersCovered = 0; // paranoid check: did we actually cover the whole range?

        foreach (var run in extent.DataRuns)
        {
            long runEndVcn = runStartVcn + run.Length; // extent-relative, exclusive

            // does this run overlap [relFirstVcn, relLastVcn) at all?
            if (runEndVcn > relFirstVcn && runStartVcn < relLastVcn)
            {
                long overlapStart = Math.Max(runStartVcn, relFirstVcn);   // extent-relative
                long overlapEnd = Math.Min(runEndVcn, relLastVcn);        // extent-relative
                long overlapClusters = overlapEnd - overlapStart;

                long runRelativeStart = overlapStart - runStartVcn;       // coordinate system #2
                long bufferByteOffset = (overlapStart - relFirstVcn) * _bytesPerCluster; // coordinate system #3
                int byteLength = (int)(overlapClusters * _bytesPerCluster);

                Span<byte> subBuffer = bufferToFill.Slice((int)bufferByteOffset, byteLength);
                ReadFromRun(run, subBuffer, runRelativeStart); // buffer's own length now drives cluster count

                clustersCovered += overlapClusters;
            }

            runStartVcn = runEndVcn;
            if (runStartVcn >= relLastVcn) break; // nothing further can overlap
        }

        if (clustersCovered != relLastVcn - relFirstVcn)
            throw new InvalidDataException(
                $"Runlist has a gap: expected to cover {relLastVcn - relFirstVcn} clusters but only found {clustersCovered}.");
    }

    public void ReadFromRun(DataRun run, Span<byte> bufferToFill, long startingVcn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startingVcn);

        if (bufferToFill.Length % _bytesPerCluster != 0)
            throw new ArgumentException(
                $"Buffer length ({bufferToFill.Length}) must be a whole number of clusters ({_bytesPerCluster} bytes each).",
                nameof(bufferToFill));

        long count = bufferToFill.Length / _bytesPerCluster;
        if (startingVcn + count > run.Length)
            throw new ArgumentOutOfRangeException(nameof(startingVcn),
                $"Attempted to read clusters {startingVcn}-{startingVcn + count} from run, which only contains {run.Length} clusters!");

        if (run.Sparse)
        {
            bufferToFill.Clear();
            return;
        }

        if (run.StartingCluster == null)
            throw new InvalidDataException("Non-sparse run has no StartingCluster set!");

        _underlying.ReadExactly((run.StartingCluster.Value + startingVcn) * _bytesPerCluster, bufferToFill);
    }
}