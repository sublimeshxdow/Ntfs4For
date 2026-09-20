using System.Buffers.Binary;
using System.Numerics;

namespace Nternal.Ntfs.Hashing;

public class SecurityHashing
{
    public static uint HashSecurityDescriptor(ReadOnlySpan<byte> data)
    {
        uint hash = 0;
        int dwordCount = data.Length >> 2;

        for (int i = 0; i < dwordCount; i++)
        {
            uint dword = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 4, 4));
            hash = BitOperations.RotateRight(hash, 0x1D) + dword;
        }

        return hash;
    }
}
