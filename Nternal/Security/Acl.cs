using Nternal.Extensions;
using System;
using System.Collections.Generic;

namespace Nternal.Ntfs.Security;

public sealed class Acl
{
    public byte AclRevision { get; }
    public List<Ace> Aces { get; }

    public Acl(ReadOnlySpan<byte> data, bool paranoid = true)
    {
        if (data.Length < 8)
            throw new InvalidDataException($"ACL header requires 8 bytes, but only got {data.Length}!");

        AclRevision = data[0];
        ushort aclSize = data.ReadUInt16(2);
        ushort aceCount = data.ReadUInt16(4);

        if (paranoid && aclSize > data.Length)
            throw new InvalidDataException(
                $"ACL declares AclSize={aclSize}, but only {data.Length} bytes are available!");

        Aces = new List<Ace>(aceCount);
        int offset = 8;

        for (int i = 0; i < aceCount; i++)
        {
            var ace = Ace.Parse(data[offset..], paranoid);
            Aces.Add(ace);
            offset += ace.Size;
        }

        if (paranoid && offset > aclSize)
            throw new InvalidDataException(
                $"Parsed {aceCount} ACEs consuming {offset} bytes, exceeding declared AclSize={aclSize}!");
    }
}
