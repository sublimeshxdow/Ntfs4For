namespace Nternal.Ntfs.NtfsExtensions.Journal;

/// <summary>
/// USN_RECORD.SourceInfo -- identifies what kind of process/subsystem
/// initiated the change, distinct from Reason's "what changed." Set via
/// FSCTL_MARK_HANDLE; per the official docs, callers typically set
/// exactly one of these values, though the field is still structurally
/// a combinable DWORD bitmask.
///
/// Declared type in the official structure: DWORD (unsigned 32-bit).
/// </summary>
[Flags]
public enum UsnSourceInfo : uint
{
    None = 0,
 
    /// <summary>
    /// A change made by the operating system's own data-management
    /// subsystem -- e.g. Remote Storage moving data between tiers.
    /// Typically accompanied by DataOverwrite in the same record's Reason,
    /// even though the data hasn't changed from the user's point of view.
    /// </summary>
    DataManagement = 0x00000001,
 
    /// <summary>
    /// The operation added a private/auxiliary data stream (e.g. a
    /// virus scanner's checksum) without changing the file's actual
    /// application data.
    /// </summary>
    AuxiliaryData = 0x00000002,
 
    /// <summary>
    /// The operation modified a file to match the contents of the same
    /// file existing on another member of a replica set (e.g. File
    /// Replication Service).
    /// </summary>
    ReplicationManagement = 0x00000004,
 
    /// <summary>
    /// The operation modified a file on a client system to match the
    /// contents of the same file existing in the cloud (cloud-sync
    /// engines, e.g. OneDrive-style placeholder rehydration). Windows
    /// 8.1+.
    /// </summary>
    ClientReplicationManagement = 0x00000008,
}
