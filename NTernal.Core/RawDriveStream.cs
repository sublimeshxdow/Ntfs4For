using Microsoft.Win32.SafeHandles;

namespace Nternal.Core;

/// <summary>
///     Represents a stream used for reading from a physical device or a logical partition.
/// </summary>
public sealed class RawDriveStream : IDataStream
{
    private readonly SafeFileHandle _handle;
    private readonly string _path;

    /// <summary>
    ///     Creates a stream that can be used to read from the specified device.
    /// </summary>
    /// <param name="devicePath">The path of the device.</param>
    public RawDriveStream(string devicePath)
    {
        _path = devicePath;
        _handle = File.OpenHandle(
            devicePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileOptions.RandomAccess);

        Alignment = (int)DiskInfo.GetSectorSizes(devicePath).Logical;
        Length = DiskInfo.GetLength(devicePath);
        FallbackAlignerStream = new UnalignedDataStream(this);
    }

    /// <summary>
    ///     For cases where the logical partition sector size is larger than the logical physical sector size
    ///     (f.e. HDDs without 512e support)
    /// </summary>
    /// <returns>A wrapper object for the designated device stream, which supports unaligned reading.</returns>
    private UnalignedDataStream FallbackAlignerStream { get; }

    public long Length { get; }
    public long AllocatedLength { get; }
    public int Alignment { get; }

    public void LoadIntoMemory()
    {
        throw new NotSupportedException("Reading an entire drive into memory is NOT supported!");
    }

    public int Read(long position, Span<byte> buffer)
    {
        EnsureOpen();
        if (position % Alignment != 0 || buffer.Length % Alignment != 0)
            return FallbackAlignerStream.Read(position, buffer);

        if (position < 0 || position > Length)
            throw new ArgumentOutOfRangeException(nameof(position));

        if (buffer.Length == 0)
            return 0;

        var remaining = Length - position;
        var count = (int)Math.Min(buffer.Length, remaining);
        return RandomAccess.Read(_handle, buffer[..count], position);
    }

    public void ReadExactly(long position, Span<byte> buffer)
    {
        EnsureOpen();
        if (position % Alignment != 0 || buffer.Length % Alignment != 0)
        {
            FallbackAlignerStream.ReadExactly(position, buffer);
            return;
        }

        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = Read(position + totalRead, buffer[totalRead..]);

            if (read == 0)
                throw new EndOfStreamException(
                    $"Could not read {buffer.Length} bytes at position {position}; " +
                    $"only {totalRead} bytes were available.");

            totalRead += read;
        }
    }

    public bool TryGetMemory(out Span<byte> memory)
    {
        EnsureOpen();
        memory = default;
        return false;
    }

    public void Dispose()
    {
        if (!_handle.IsClosed)
            _handle.Dispose();
    }

    private void EnsureOpen()
    {
        if (_handle.IsClosed)
            throw new InvalidOperationException($"The handle to \"{_path}\" has been closed already!");
    }
}