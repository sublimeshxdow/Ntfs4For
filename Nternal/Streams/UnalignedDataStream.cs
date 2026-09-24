using Nternal.Core.Streams;

namespace Nternal.Streams;

public class UnalignedDataStream : IDataStream
{
    public long Length => _source.Length;
    public long AllocatedLength => _source.AllocatedLength;
    public int Alignment => 1;

    private IDataStream _source { get; }
    private bool _leaveOpen;

    public UnalignedDataStream(IDataStream underlying, bool leaveOpen = false)
    {
        _source = underlying;
        _leaveOpen = leaveOpen;
    }

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

        long startingBlock = position / _source.Alignment;
        long endingBlock = (position + buffer.Length + _source.Alignment - 1) / _source.Alignment;

        long startOffsetToRead = startingBlock * _source.Alignment;
        long endOffsetToRead = endingBlock * _source.Alignment;
        int offsetWithinFirstBlock = (int)(position - startOffsetToRead);

        Span<byte> alignedRead = new byte[endOffsetToRead - startOffsetToRead];
        int read = _source.Read(startOffsetToRead, alignedRead);

        int available = Math.Max(0, Math.Min(buffer.Length, read - offsetWithinFirstBlock));
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