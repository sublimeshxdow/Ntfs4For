namespace Nternal.Ntfs;

public partial class NtfsVolume
{
    private void ReadBytes(long offset, Span<byte> buffer)
    {
        _provider.ReadExactly(offset, buffer);
    }

    private void ReadSectors(long startingSector, Span<byte> buffer)
    {
        _provider.ReadExactly(startingSector * BootSector.BytesPerSector, buffer);
    }

    private void ReadClusters(long startingCluster, Span<byte> buffer)
    {
        _provider.ReadExactly(startingCluster * BootSector.BytesPerCluster, buffer);
    }
}