namespace Nternal.Ntfs.Security;

public enum AceType : byte
{
    AccessAllowed = 0x00,
    AccessDenied = 0x01,
    SystemAudit = 0x02,
 
    /// <summary>Defined by the spec, but never actually used by Windows.</summary>
    SystemAlarm = 0x03,
 
    AccessAllowedObject = 0x05,
    AccessDeniedObject = 0x06,
    SystemAuditObject = 0x07,
    AccessAllowedCallback = 0x09,
    AccessDeniedCallback = 0x0A,
    AccessAllowedCallbackObject = 0x0B,
    AccessDeniedCallbackObject = 0x0C,
    SystemAuditCallback = 0x0D,
 
    /// <summary>Windows Integrity Levels (Mandatory Integrity Control).</summary>
    SystemMandatoryLabel = 0x11,
 
    /// <summary>Dynamic Access Control claims.</summary>
    SystemResourceAttribute = 0x12,
 
    SystemScopedPolicyId = 0x13
}
