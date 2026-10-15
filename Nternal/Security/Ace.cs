using Nternal.Extensions;
using System;
using System.Security.AccessControl;

namespace Nternal.Ntfs.Security;

public class Ace
{
    public AceType Type { get; }
    public AceFlags Flags { get; }
    internal ushort Size { get; }
    public FileSystemRights AccessMask { get; }
    public Sid Sid { get; }

    private static readonly AceType[] ObjectAceTypes =
    {
        AceType.AccessAllowedObject,
        AceType.AccessDeniedObject,
        AceType.SystemAuditObject,
        AceType.AccessAllowedCallbackObject,
        AceType.AccessDeniedCallbackObject,
    };

    public static Ace Parse(ReadOnlySpan<byte> data, bool paranoid = true)
    {
        if (data.Length < 4)
            throw new InvalidDataException($"ACE_HEADER requires 4 bytes, but only got {data.Length}!");

        AceType type = (AceType)data[0];
        ushort size = data.ReadUInt16(2);

        if (paranoid && size > data.Length)
            throw new InvalidDataException(
                $"ACE declares Size={size}, but only {data.Length} bytes are available!");

        ReadOnlySpan<byte> aceData = data[..size];

        return Array.IndexOf(ObjectAceTypes, type) >= 0
            ? new ObjectAce(aceData, paranoid)
            : new Ace(aceData, sidOffset: 8, paranoid);
    }

    protected Ace(ReadOnlySpan<byte> aceData, int sidOffset, bool paranoid)
    {
        if (paranoid && aceData.Length < sidOffset)
            throw new InvalidDataException(
                $"ACE of size {aceData.Length} is too short to contain a SID at offset {sidOffset}!");

        Type = (AceType)aceData[0];
        Flags = (AceFlags)aceData[1];
        Size = aceData.ReadUInt16(2);
        AccessMask = (FileSystemRights)aceData.ReadUInt32(4);
        Sid = new Sid(aceData[sidOffset..], paranoid);
    }
}

public sealed class ObjectAce : Ace
{
    public AceObjectFlags ObjectFlags { get; }
    public Guid? ObjectType { get; }
    public Guid? InheritedObjectType { get; }

    internal ObjectAce(ReadOnlySpan<byte> aceData, bool paranoid)
        : base(aceData,
            ComputeSidOffset(aceData, paranoid, out var objectFlags, out var objectType, out var inheritedObjectType),
            paranoid)
    {
        ObjectFlags = objectFlags;
        ObjectType = objectType;
        InheritedObjectType = inheritedObjectType;
    }

    private static int ComputeSidOffset(
        ReadOnlySpan<byte> aceData, bool paranoid,
        out AceObjectFlags objectFlags, out Guid? objectType, out Guid? inheritedObjectType)
    {
        if (paranoid && aceData.Length < 12)
            throw new InvalidDataException(
                $"Object ACE requires at least 12 bytes before its GUIDs, but got {aceData.Length}!");

        objectFlags = (AceObjectFlags)aceData.ReadUInt32(8);
        int offset = 12;

        objectType = null;
        if (objectFlags.HasFlag(AceObjectFlags.ObjectTypePresent))
        {
            objectType = new Guid(aceData.Slice(offset, 16));
            offset += 16;
        }

        inheritedObjectType = null;
        if (objectFlags.HasFlag(AceObjectFlags.InheritedObjectTypePresent))
        {
            inheritedObjectType = new Guid(aceData.Slice(offset, 16));
            offset += 16;
        }

        return offset;
    }
}