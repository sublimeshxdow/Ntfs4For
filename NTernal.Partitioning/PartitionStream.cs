using Nternal.Core;

namespace Nternal.Partitioning;

/// <summary>
///     Represents a partition that can be read from.
/// </summary>
public class PartitionStream : IDataStream
{
    /// <summary>
    ///     Creates a new stream of a partition that can be read from.
    /// </summary>
    /// <param name="underlying">The underlying stream of the full device.</param>
    /// <param name="partition">The partition entry to create a stream for.</param>
    public PartitionStream(IDataStream underlying, IPartitionEntry partition)
    {
        Underlying = underlying;
        Entry = partition;
    }

    private IDataStream Underlying { get; }
    private IPartitionEntry Entry { get; }
    public long Length => (long)(Entry.InclusiveEndingLba - Entry.StartingLba + 1) * Alignment;
    public long AllocatedLength => Length;
    public int Alignment => Underlying.Alignment;

    public void LoadIntoMemory()
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
    }

    public int Read(long position, Span<byte> buffer)
    {
        if (position < 0)
            throw new ArgumentOutOfRangeException(nameof(position), "Position cannot be negative.");

        if (position % Alignment != 0 || buffer.Length % Alignment != 0)
            throw new ArgumentException($"Position and buffer length must be aligned to the sector size ({Alignment}).",
                nameof(buffer));

        if (position >= Length)
            return 0;

        var length = (int)Math.Min(buffer.Length, Length - position);
        var absolutePosition = (long)Entry.StartingLba * Alignment + position;
        return Underlying.Read(absolutePosition, buffer.Slice(0, length));
    }

    public void ReadExactly(long position, Span<byte> buffer)
    {
        var read = Read(position, buffer);
        if (read != buffer.Length)
            throw new InvalidOperationException(
                $"Failed to read the exact number of bytes (required={buffer.Length}, read={read}).");
    }

    public bool TryGetMemory(out Span<byte> memory)
    {
        if (Underlying.TryGetMemory(out var underlying))
        {
            memory = underlying.Slice((int)((long)Entry.StartingLba * Alignment), (int)Length);
            return true;
        }

        memory = default;
        return false;
    }
}