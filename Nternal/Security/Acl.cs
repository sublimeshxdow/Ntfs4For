using Nternal.Extensions;
using System;
using System.Collections.Generic;

namespace Nternal.Ntfs.Security;

public sealed class Acl
{
    public byte AclRevision { get; }
    public ushort AclSize { get; }
    public ushort DeclaredAceCount { get; }
    public List<Ace> Aces { get; }

    public Acl(ReadOnlySpan<byte> data, bool paranoid = true)
    {
        if (data.Length < 8)
            throw new InvalidDataException($"ACL header requires 8 bytes, but only got {data.Length}!");

        AclRevision = data[0];
        AclSize = data.ReadUInt16(2);
        DeclaredAceCount = data.ReadUInt16(4);

        if (paranoid && AclSize > data.Length)
            throw new InvalidDataException(
                $"ACL declares AclSize={AclSize}, but only {data.Length} bytes are available!");

        Aces = new List<Ace>(DeclaredAceCount);
        int offset = 8;

        for (int i = 0; i < DeclaredAceCount; i++)
        {
            var ace = Ace.Parse(data[offset..], paranoid);
            Aces.Add(ace);
            offset += ace.Size;
        }

        if (paranoid && offset > AclSize)
            throw new InvalidDataException(
                $"Parsed {DeclaredAceCount} ACEs consuming {offset} bytes, exceeding declared AclSize={AclSize}!");
    }
}
