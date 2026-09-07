using Nternal.Extensions;
using System;

namespace Nternal.Ntfs.Security;

public sealed class SecurityDescriptor
{
    public byte Revision { get; }
    public SecurityDescriptorControl Control { get; }
    public Sid? Owner { get; }
    public Sid? Group { get; }
    public Acl? Sacl { get; }
    public Acl? Dacl { get; }

    public SecurityDescriptor(ReadOnlySpan<byte> data, bool paranoid = true)
    {
        if (data.Length < 0x14)
            throw new InvalidDataException(
                $"SECURITY_DESCRIPTOR header requires 20 bytes, but only got {data.Length}!");

        Revision = data[0];
        Control = (SecurityDescriptorControl)data.ReadUInt16(2);

        // On disk, this must ALWAYS be self-relative -- the absolute form
        // contains raw pointers that cannot be persisted or transmitted.
        if (paranoid && !Control.HasFlag(SecurityDescriptorControl.SelfRelative))
            throw new InvalidDataException(
                "SECURITY_DESCRIPTOR is not self-relative -- cannot be parsed from a flat byte buffer.");

        uint ownerOffset = data.ReadUInt32(4);
        uint groupOffset = data.ReadUInt32(8);
        uint saclOffset = data.ReadUInt32(12);
        uint daclOffset = data.ReadUInt32(16);

        // Owner/Group are normally always present on a valid SD; the
        // null-check here is purely defensive against malformed data.
        Owner = ownerOffset != 0 ? new Sid(data[(int)ownerOffset..], paranoid) : null;
        Group = groupOffset != 0 ? new Sid(data[(int)groupOffset..], paranoid) : null;

        // Per MS-DTYP: if the *Present control bit is unset, the offset
        // field MUST be zero and the corresponding ACL MUST NOT be parsed
        // -- check both, not just whichever one happens to look truthy.
        Sacl = Control.HasFlag(SecurityDescriptorControl.SaclPresent) && saclOffset != 0
            ? new Acl(data[(int)saclOffset..], paranoid)
            : null;

        Dacl = Control.HasFlag(SecurityDescriptorControl.DaclPresent) && daclOffset != 0
            ? new Acl(data[(int)daclOffset..], paranoid)
            : null;
    }
}
