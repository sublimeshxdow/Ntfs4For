namespace Nternal.Ntfs.Generic;

public readonly record struct FileReference
{
    private const ulong FileRecordIdMask = 0x0000FFFFFFFFFFFF;

    public readonly ulong FileReferenceId = 0xFFFFFFFFFFFFFFFF;

    public FileReference(ulong refNumber)
    {
        FileReferenceId = refNumber;
    }

    public FileReference(ulong fileRecordId, ushort sequenceNumber)
    {
        if (fileRecordId > FileRecordIdMask)
            throw new ArgumentOutOfRangeException(nameof(fileRecordId), fileRecordId,
                $"Must be at most 0x{FileRecordIdMask:X}.");

        FileReferenceId = fileRecordId | ((ulong)sequenceNumber << 48);
    }

    public ulong FileRecordId => FileReferenceId & FileRecordIdMask;
    public ushort SequenceNumber => (ushort)(FileReferenceId >> 48);

    public override string ToString()
    {
        return $"FileRecordId={FileRecordId} Usn={SequenceNumber}";
    }
}