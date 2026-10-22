namespace Nternal.Core;

/// <summary>
///     Represents a pseudo-stream encapsulating another stream which does not support individual byte-by-byte reads.
/// </summary>
public class UnalignedDataStream : IDataStream
{
    private readonly bool _leaveOpen;

    /// <summary>
    ///     Creates a new stream supporting reads the underlying stream might not support due to alignment issues.
    /// </summary>
    /// <param name="underlying">The stream that should be encapsulated.</param>
    /// <param name="leaveOpen">Whether the underlying stream should be disposed alongside this instance.</param>
    public UnalignedDataStream(IDataStream underlying, bool leaveOpen = false)
    {
        _source = underlying;
        _leaveOpen = leaveOpen;
    }

    private IDataStream _source { get; }
    public long Length => _source.Length;
    public long AllocatedLength => _source.AllocatedLength;
    public int Alignment => 1;

    public void LoadIntoMemory()
    {
        _source.LoadIntoMemory();
    }

    public int Read(long position, Span<byte> buffer)
    {
        if (buffer.Length == 0)
            return 0;

        if (position % _source.Alignment == 0 && buffer.Length % _source.Alignment == 0)
            return _source.Read(position, buffer);

        var startingBlock = position / _source.Alignment;
        var endingBlock = (position + buffer.Length + _source.Alignment - 1) / _source.Alignment;

        var startOffsetToRead = startingBlock * _source.Alignment;
        var endOffsetToRead = endingBlock * _source.Alignment;
        var offsetWithinFirstBlock = (int)(position - startOffsetToRead);

        Span<byte> alignedRead = new byte[endOffsetToRead - startOffsetToRead];
        var read = _source.Read(startOffsetToRead, alignedRead);

        var available = Math.Max(0, Math.Min(buffer.Length, read - offsetWithinFirstBlock));
        alignedRead.Slice(offsetWithinFirstBlock, available).CopyTo(buffer);
        return available;
    }

    public void ReadExactly(long position, Span<byte> buffer)
    {
        if (Read(position, buffer) != buffer.Length)
            throw new EndOfStreamException();
    }

    public bool TryGetMemory(out Span<byte> memory)
    {
        return _source.TryGetMemory(out memory);
    }

    public void Dispose()
    {
        if (!_leaveOpen)
            _source.Dispose();
    }
}