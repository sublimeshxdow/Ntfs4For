namespace Nternal.Core.Streams
{
    /// <summary>
    /// The base interface used for representing a readable object.
    /// </summary>
    public interface IDataStream : IDisposable
    {
        /// <summary>
        /// The amount of bytes that can be read from this object.
        /// </summary>
        long Length { get; }
        /// <summary>
        /// The amount of bytes reserved by the underlying stream for representing this object.
        /// </summary>
        long AllocatedLength { get; }
        /// <summary>
        /// The amount of bytes the offset/length must be aligned to with reading.
        /// </summary>
        int Alignment { get; }

        /// <summary>
        /// Attempts to load the entire stream into memory.
        /// </summary>
        void LoadIntoMemory();

        /// <summary>
        /// Reads bytes of the current object into a buffer.
        /// </summary>
        /// <param name="position">The offset of where to start reading from.</param>
        /// <param name="buffer">The buffer to read the bytes into. The size of the buffer determines how many bytes to read.</param>
        /// <returns>Returns the amount of bytes read.</returns>
        int Read(long position, Span<byte> buffer);
        /// <summary>
        /// Attempts to read exactly <length of buffer> bytes into a buffer.
        /// If the stream does not contain enough bytes, an exception is thrown.
        /// </summary>
        /// <param name="position">The offset of where to start reading from.</param>
        /// <param name="buffer">The buffer to read the bytes into. The size of the buffer determines how many bytes to read.</param>
        void ReadExactly(long position, Span<byte> buffer);

        /// <summary>
        /// Attempts to query a span of bytes representing the memory governed by this object.
        /// </summary>
        /// <param name="memory">The span of bytes referencing the object's memory.</param>
        /// <returns>Whether the raw memory could be retrieved.</returns>
        bool TryGetMemory(out Span<byte> memory);
    }
}
