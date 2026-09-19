namespace Nternal.Ntfs.Security;

[Flags]
public enum AceFlags : byte
{
    None = 0,
 
    /// <summary>Non-container child objects inherit this ACE as effective.</summary>
    ObjectInherit = 0x01,
 
    /// <summary>Container child objects inherit this ACE as effective.</summary>
    ContainerInherit = 0x02,
 
    /// <summary>Strips ObjectInherit/ContainerInherit from the ACE once inherited once.</summary>
    NoPropagateInherit = 0x04,
 
    /// <summary>Does not control access to the object it's attached to -- only to children.</summary>
    InheritOnly = 0x08,
 
    /// <summary>This ACE was propagated down from a parent, not set directly.</summary>
    Inherited = 0x10,
 
    /// <summary>SACL only: generate an audit entry on successful access.</summary>
    SuccessfulAccess = 0x40,
 
    /// <summary>SACL only: generate an audit entry on failed access.</summary>
    FailedAccess = 0x80
}
