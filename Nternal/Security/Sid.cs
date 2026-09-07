using Nternal.Extensions;
using System;
using System.Linq;

namespace Nternal.Ntfs.Security;

public sealed class Sid
{
    public byte Revision { get; }
    public byte SubAuthorityCount { get; }
    public ulong IdentifierAuthority { get; }
    public uint[] SubAuthorities { get; }

    /// <summary>Total byte length actually consumed by this SID.</summary>
    public int Length => 8 + SubAuthorities.Length * 4;

    public Sid(ReadOnlySpan<byte> data, bool paranoid = true)
    {
        if (data.Length < 8)
            throw new InvalidDataException($"SID requires at least 8 bytes, but only got {data.Length}!");

        Revision = data[0];
        SubAuthorityCount = data[1];

        ulong authority = 0;
        for (int i = 0; i < 6; i++)
            authority = (authority << 8) | data[2 + i];
        IdentifierAuthority = authority;

        if (paranoid && data.Length < 8 + SubAuthorityCount * 4)
            throw new InvalidDataException(
                $"SID declares {SubAuthorityCount} sub-authorities, but buffer only has {data.Length - 8} bytes left!");

        SubAuthorities = new uint[SubAuthorityCount];
        for (int i = 0; i < SubAuthorityCount; i++)
            SubAuthorities[i] = data.ReadUInt32(8 + i * 4);
    }

    public override string ToString() =>
        $"S-{Revision}-{IdentifierAuthority}-{string.Join('-', SubAuthorities)}";
}
