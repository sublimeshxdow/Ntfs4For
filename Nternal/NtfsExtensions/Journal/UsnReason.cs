namespace Nternal.Ntfs.NtfsExtensions.Journal;

/// <summary>
///     USN_RECORD.Reason -- describes what kind of change(s) triggered this
///     record. Multiple bits are commonly set at once (e.g. a rename sets
///     both RenameOldName/RenameNewName across a pair of records, alongside
///     Close on the final one).
///     Declared type in the official structure: DWORD (unsigned 32-bit).
/// </summary>
[Flags]
public enum UsnReason : uint
{
    None = 0,

    /// <summary>Data in the unnamed data stream was overwritten.</summary>
    DataOverwrite = 0x00000001,

    /// <summary>The file or directory was extended (added to).</summary>
    DataExtend = 0x00000002,

    /// <summary>The file or directory was truncated.</summary>
    DataTruncation = 0x00000004,

    /// <summary>Data in a named data stream was overwritten.</summary>
    NamedDataOverwrite = 0x00000010,

    /// <summary>A named data stream was extended (added to).</summary>
    NamedDataExtend = 0x00000020,

    /// <summary>A named data stream was truncated.</summary>
    NamedDataTruncation = 0x00000040,

    /// <summary>The file or directory was created for the first time.</summary>
    FileCreate = 0x00000100,

    /// <summary>The file or directory was deleted.</summary>
    FileDelete = 0x00000200,

    /// <summary>
    ///     Extended attributes changed. Not accessible to ordinary
    ///     Windows-based applications.
    /// </summary>
    EaChange = 0x00000400,

    /// <summary>Access rights to the file or directory were changed.</summary>
    SecurityChange = 0x00000800,

    /// <summary>Renamed -- the name in this record is the PREVIOUS name.</summary>
    RenameOldName = 0x00001000,

    /// <summary>Renamed -- the name in this record is the NEW name.</summary>
    RenameNewName = 0x00002000,

    /// <summary>The FILE_ATTRIBUTE_NOT_CONTENT_INDEXED attribute was toggled.</summary>
    IndexableChange = 0x00004000,

    /// <summary>One or more attributes or timestamps were changed.</summary>
    BasicInfoChange = 0x00008000,

    /// <summary>An NTFS hard link was added to or removed from the file.</summary>
    HardLinkChange = 0x00010000,

    /// <summary>The compression state was changed to or from compressed.</summary>
    CompressionChange = 0x00020000,

    /// <summary>The file or directory was encrypted or decrypted.</summary>
    EncryptionChange = 0x00040000,

    /// <summary>The object identifier ($OBJECT_ID) was changed.</summary>
    ObjectIdChange = 0x00080000,

    /// <summary>A reparse point was changed, added, or deleted.</summary>
    ReparsePointChange = 0x00100000,

    /// <summary>A named stream was added, removed, or renamed.</summary>
    StreamChange = 0x00200000,

    /// <summary>The given stream is modified through a committed TxF transaction.</summary>
    TransactedChange = 0x00400000,

    /// <summary>
    ///     The FILE_ATTRIBUTE_INTEGRITY_STREAM attribute's state changed for
    ///     the given stream. ReFS-specific: integrity streams maintain a
    ///     checksum of the stream's data for read/write validation. Present
    ///     in the enum for completeness since the bit is structurally valid
    ///     in any USN_RECORD, but only meaningfully set on ReFS volumes.
    /// </summary>
    IntegrityChange = 0x00800000,

    // Bits 0x01000000 through 0x40000000 remain undocumented in every
    // source used to build this enum (official MS docs included, as of
    // the USN_RECORD_V4 page). Treat an unrecognized bit here as
    // "unknown," not evidence of corruption.

    /// <summary>The file or directory was closed. Commonly the final record in a burst of changes.</summary>
    Close = 0x80000000
}