namespace Nternal.Partitioning.Mbr;

/// <summary>
///     Represents a location within an HDD on a CHS (Cylinder-Head-Sector) - basis.
/// </summary>
public struct ChsAddress
{
    /// <summary>
    ///     Constructs a new CHS-Entry based on the given bytes.
    /// </summary>
    /// <param name="bytes">The CHS-bytes to construct the entry from.</param>
    /// <exception cref="ArgumentException">Throws if the passed buffer is not exactly 3 bytes large.</exception>
    public ChsAddress(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 3)
            throw new ArgumentException("CHS address must be exactly 3 bytes long.", nameof(bytes));

        Head = bytes[0];
        Sector = (byte)(bytes[1] & 0x3F);
        Cylinder = (ushort)(((bytes[1] & 0xC0) << 2) | bytes[2]);
    }

    public byte Head { get; init; }
    public byte Sector { get; init; }
    public ushort Cylinder { get; init; }
}