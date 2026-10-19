using Nternal.Structs;
using System;
using System.Collections.Generic;
using System.Text;

namespace DeletedFileRecovery
{
    internal sealed record DeletedEntry(
        long RecordNumber,
        ushort SequenceNumber,
        string Name,
        FileReference Parent,
        bool IsDirectory,
        long Size,
        DateTime Modified,
        Recoverability State,
        string? Error);
}
