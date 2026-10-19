using Nternal;
using Nternal.Ntfs;
using Nternal.Structs;
using System;
using System.Collections.Generic;
using System.Text;

namespace DeletedFileRecovery
{

    /// <summary>
    /// Rebuilds the folder a deleted file lived in. The parent may still exist (normal path lookup),
    /// or may itself be deleted - in which case its name is taken from the deleted folder records we scanned.
    /// </summary>
    internal sealed class DeletedPathResolver
    {
        private readonly NtfsVolume _volume;
        private readonly Dictionary<long, DeletedEntry> _deletedFolders;
        private readonly Dictionary<FileReference, string> _cache = new();

        public DeletedPathResolver(NtfsVolume volume, List<DeletedEntry> entries)
        {
            _volume = volume;
            _deletedFolders = entries.Where(e => e.IsDirectory).ToDictionary(e => e.RecordNumber);
        }

        public string GetFolder(DeletedEntry entry) => entry.State == Recoverability.Unreadable
            ? "<unknown>"
            : Resolve(entry.Parent, 0);

        private string Resolve(FileReference folder, int depth)
        {
            if (_cache.TryGetValue(folder, out var cached))
                return cached;

            string path;
            if (depth > 64)
            {
                path = "<too deep>";
            }

            else if (TryLiveFolder(folder, out var livePath))
            {
                path = livePath;
            }

            // Deleting bumps the record's sequence number by one, so the parent reference stored in
            // its children is exactly one behind.
            else if (_deletedFolders.TryGetValue((long)folder.FileRecordId, out var deleted)
                     && (ushort)(folder.SequenceNumber + 1) == deleted.SequenceNumber)
            {
                path = $"{Resolve(deleted.Parent, depth + 1)}/{deleted.Name} [deleted]";
            }

            else
            {
                path = $"<lost folder #{folder.FileRecordId}>";
            }

            _cache[folder] = path;
            return path;
        }

        private bool TryLiveFolder(FileReference folder, out string path)
        {
            path = "";
            try
            {
                if (folder.FileRecordId == (ulong)KnownMftRecord.Root)
                    return true; // path stays "" so children become "/name"

                // Sequence numbers must match - otherwise the record now belongs to a different folder.
                var file = _volume.GetNtfsFile(folder, compareSequence: true);
                if (!file.RecordHeader.Flags.HasFlag(FileRecordFlags.IsInUse | FileRecordFlags.IsDirectory))
                    return false;

                path = _volume.GetPrimaryAbsolutePath(folder);
                return !path.StartsWith("??");
            }

            catch
            {
                return false;
            }
        }
    }
}
