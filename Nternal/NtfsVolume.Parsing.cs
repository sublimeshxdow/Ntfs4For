using System;
using System.Collections.Generic;
using System.Text;
using Nternal.Attributes;
using Nternal.Core.Streams;
using Nternal.Helpers;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Index;
using Nternal.Structs;

namespace Nternal
{
    public partial class NtfsVolume
    {
        public RawFileRecord GetRawRecordData(FileReference reference, bool paranoid = true,
            bool compareSequence = true, bool fixup = true, IDataStream? mft = null)
        {
            if (_hasParsedMftYet)
            {
                var bitmapAttr = MftFile.GetAttributeData<BitmapAttribute>(NtfsAttributeType.Bitmap, "");
                if (bitmapAttr == null)
                    throw new InvalidOperationException(
                        $"$Mft does not seem to have an unnamed bitmap-attribute - corruption detected.");

                if (!bitmapAttr.IsSet((long)reference.FileRecordId))
                    throw new Exception($"Attempted to parse an unused $Mft-Record!");
            }

            var mftStream = mft ?? MftFile.GetAttributeData<DataAttribute>(NtfsAttributeType.Data, "").GetStream();
            if (mftStream == null)
                throw new InvalidOperationException(
                    $"$Mft does not seem to have an unnamed data-attribute - corruption detected.");

            long clusterWithRecord = (long)reference.FileRecordId / 4;
            int index = (int)(reference.FileRecordId % 4);
            int recordOffset = index * BootSector.FileRecordSize;

            Span<byte> fileRecordCluster = new byte[BootSector.BytesPerCluster];
            mftStream.ReadExactly(clusterWithRecord * BootSector.BytesPerCluster, fileRecordCluster);
            Span<byte> fileRecord = fileRecordCluster[recordOffset..(recordOffset + BootSector.FileRecordSize)];

            if (fixup)
                FixupHelper.FixupRecord(fileRecord);

            var rawRecord = new RawFileRecord(fileRecord);
            if (compareSequence && rawRecord.SequenceNumber != reference.SequenceNumber)
                throw new ArgumentException(
                    $"Attempted to parse record for file-reference {reference}, but that doesn't match with the record's sequence-number {rawRecord.SequenceNumber}!");

            return rawRecord;
        }

        public NtfsFile ParseFile(FileReference reference)
        {
            var raw = GetRawRecordData(reference);
            return new NtfsFile(raw, _provider, this);
        }

        public void ParseMft(bool paranoid = true, bool fixup = true, bool postXp = true)
        {
            var raw = GetMftRecord(paranoid, fixup);
            MftFile = new NtfsFile(raw, _provider, this, postXp);
            _hasParsedMftYet = true;
        }

        public NtfsFile GetKnownNtfsFile(KnownMftRecord record)
        {
            var fileReference = new FileReference((ulong)record, (ushort)Math.Max((ulong)record, 1));
            return GetNtfsFile(fileReference, true);
        }

        public NtfsFile GetNtfsFile(FileReference reference, bool compareSequence = true)
        {
            var rawRecord = GetRawRecordData(reference, true, compareSequence, true);
            return new NtfsFile(rawRecord, _provider, this, true);
        }

        public NtfsFile GetNtfsFile(string path)
        {
            if (RootFolder == null)
                throw new InvalidOperationException(
                    $"Cannot parse arbitrary NTFS files before initialization completed!");

            path = path.Trim('/');
            string[] parts = path.Split('/');
            List<FileReference> references = new(path.Split('/').Length)
            {
                RootFolder.GetReference()
            };

            NtfsFile file = RootFolder;
            string currentPath = "/";

            foreach (string entry in path.Split('/'))
            {
                // Check for symlinks
                var reparse = file.NtfsAttributes
                    .FirstOrDefault(x => x.Type == NtfsAttributeType.Reparse);

                if (reparse != null)
                {
                    var reparseData = reparse.Cast<ReparseAttribute>(true);
                    if (reparseData.TypeAndFlags.HasFlag(ReparseTypeAndFlags.SymbolicLink))
                    {
                        var symlinkData = reparseData.Data as SymbolicLinkReparseData;
                        if (symlinkData == null)
                            throw new InvalidOperationException(
                                $"File at {path} has $Reparse-Attribute of type SymbolicLink, but it's data is of another type!");

                        string substitutePath = symlinkData.SubstituteName;
                        if (!symlinkData.Flags.HasFlag(SymbolicLinkReparseData.SymlinkFlags.IsRelative))
                            throw new NotSupportedException(
                                $"At the moment, only relative symbolic links are supported!\n" +
                                $"This path would resolve to \"{Path.Combine(substitutePath, path.Replace(currentPath, ""))}\".");

                        if (substitutePath.StartsWith('\\'))
                        {
                            substitutePath = substitutePath.Replace('\\', '/');
                            return GetNtfsFile(substitutePath);
                        }

                        string[] reparsePathParts = substitutePath.Split('\\');
                        foreach (var reparsePath in reparsePathParts)
                        {
                            if (reparsePath == "..")
                            {
                                int nextLowerPath = reparsePath.LastIndexOf('/');
                                if (nextLowerPath == -1)
                                    throw new FileNotFoundException(
                                        $"The given file cannot exist, as it's relative symbolic link resolves to a path that cannot exis!");

                                currentPath = currentPath[..(nextLowerPath - 1)];
                            }

                            else
                                return GetNtfsFile(currentPath);
                        }
                    }
                }

                if (!file.FileRecord.Flags.HasFlag(FileRecordFlags.IsDirectory))
                    throw new InvalidOperationException($"\"{currentPath}\" does not represent a directory!");

                IndexParser parser = CreateIndexParser(file, "$I30");
                var indexEntry = parser.TryFindRootEntry(Encoding.Unicode.GetBytes(entry));
                if (indexEntry == null)
                    throw new FileNotFoundException($"Failed to find file/folder \"{entry}\" at \"{currentPath}\"!");

                var reference = indexEntry.GetI30MftReference()!.Value;
                if (references.Contains(reference))
                    throw new InvalidOperationException($"Detected circular folder structure - will not continue!");

                references.Add(reference);
                file = GetNtfsFile(reference, true);
                currentPath += $"/{entry}";
            }

            return file;
        }

        private RawFileRecord GetMftRecord(bool paranoid = true, bool fixup = true)
        {
            Span<byte> mftRecord = BootSector.FileRecordSize <= 1024
                ? stackalloc byte[BootSector.FileRecordSize]
                : new byte[BootSector.FileRecordSize];

            _provider.ReadExactly(BootSector.MftClusterNumber * BootSector.BytesPerCluster, mftRecord);

            if (fixup)
                FixupHelper.FixupRecord(mftRecord);

            return new RawFileRecord(mftRecord);
        }

        private IndexParser CreateIndexParser(NtfsFile file, string index) =>
            new IndexParser(file, index, CollationHandler, FixupHelper);
    }
}