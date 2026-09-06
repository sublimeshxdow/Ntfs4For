namespace Nternal.Ntfs.Security;

[Flags]
public enum AceObjectFlags : uint
{
    None = 0,
 
    /// <summary>ObjectType GUID is present.</summary>
    ObjectTypePresent = 0x00000001,
 
    /// <summary>InheritedObjectType GUID is present.</summary>
    InheritedObjectTypePresent = 0x00000002,
}
