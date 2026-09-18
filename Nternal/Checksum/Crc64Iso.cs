namespace Nternal.Ntfs.Checksum;

// CRC-64/ISO-HDLC (ISO 3309), reflected, seed = 0, no final XOR.
// Public domain — algorithm only, no copyrighted source reused.
public static class RtlCrc64Nvme
{
    // Reflected Rocksoft/CRC-64/NVMe polynomial (normal form 0xAD93D23594C93659)
    private const ulong Poly = 0x9A6C9329AC4BC9B5UL;
    private const ulong Mask = 0xFFFFFFFFFFFFFFFFUL; // ctrl+0x20 pre/post XOR

    private static readonly ulong[] Table = BuildTable();

    private static ulong[] BuildTable()
    {
        var table = new ulong[256];
        for (int i = 0; i < 256; i++)
        {
            ulong crc = (ulong)i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Poly : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    // Mirrors RtlCrc64(Buffer, Size, InitialCrc)
    public static ulong Compute(ReadOnlySpan<byte> data, ulong initialCrc = 0)
    {
        ulong crc = Mask ^ initialCrc;      // rax_1 = rbp ^ arg3
        foreach (byte b in data)
            crc = Table[(byte)(crc ^ b)] ^ (crc >> 8);
        return crc ^ Mask;                  // return rax_1 ^ rbp
    }
}
