namespace Nternal.Ntfs.Security;

[Flags]
public enum SecurityDescriptorControl : ushort
{
    None = 0,

    /// <summary>Owner SID was provided by a default mechanism, not the original caller.</summary>
    OwnerDefaulted = 0x0001,

    /// <summary>Group SID was provided by a default mechanism.</summary>
    GroupDefaulted = 0x0002,

    /// <summary>A DACL is present. If unset (or set with a NULL DACL), the object allows full access to everyone.</summary>
    DaclPresent = 0x0004,

    /// <summary>DACL was provided by a default mechanism.</summary>
    DaclDefaulted = 0x0008,

    /// <summary>A SACL is present.</summary>
    SaclPresent = 0x0010,

    /// <summary>SACL was provided by a default mechanism.</summary>
    SaclDefaulted = 0x0020,

    /// <summary>The DACL came from an untrusted source; triggers SID substitution for compound ACEs.</summary>
    DaclUntrusted = 0x0040,

    /// <summary>Requests compound-ACE conversion for server-side access checks.</summary>
    ServerSecurity = 0x0080,

    DaclAutoInheritReq = 0x0100,
    SaclAutoInheritReq = 0x0200,

    /// <summary>DACL was set via automatic inheritance.</summary>
    DaclAutoInherited = 0x0400,

    /// <summary>SACL was set via automatic inheritance.</summary>
    SaclAutoInherited = 0x0800,

    /// <summary>Blocks inheritable ACEs from modifying this DACL.</summary>
    DaclProtected = 0x1000,

    /// <summary>Blocks inheritable ACEs from modifying this SACL.</summary>
    SaclProtected = 0x2000,

    /// <summary>A resource-manager-specific control field is valid. Rarely used on disk.</summary>
    RmControlValid = 0x4000,

    /// <summary>
    ///     Self-relative form (offsets, not pointers). ALWAYS set for anything
    ///     persisted to disk -- the absolute form (raw pointers) cannot be
    ///     stored or transmitted at all.
    /// </summary>
    SelfRelative = 0x8000
}