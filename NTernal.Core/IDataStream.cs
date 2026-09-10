namespace Nternal.Core.Streams
{
    public interface IDataStream : IDisposable
    {
        long Length { get; }
        long AllocatedLength { get; }
        int Alignment { get; }

        int Read(long position, Span<byte> buffer);
        void ReadExactly(long position, Span<byte> buffer);

        bool TryGetMemory(out Span<byte> memory);
    }
}
