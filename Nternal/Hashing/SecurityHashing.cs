using System.Buffers.Binary;
using System.Numerics;

namespace Nternal.Ntfs.Hashing;

public class SecurityHashing
{
    public static uint HashSecurityDescriptor(ReadOnlySpan<byte> buffer)
    {
        uint hash = 0;
        while (buffer.Length >= 4)
        {
            hash = BitOperations.RotateRight(hash, 29);
            hash += BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            buffer = buffer[4..];
        }

        return hash;
    }
}
