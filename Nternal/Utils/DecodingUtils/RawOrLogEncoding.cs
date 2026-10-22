namespace Nternal.Ntfs.Utils.DecodingUtils;

internal static class RawOrLogEncoding
{
    internal static int ResolveSize(sbyte raw, int bytesPerCluster)
    {
        return raw > 0 ? raw * bytesPerCluster : 1 << -raw;
    }
}