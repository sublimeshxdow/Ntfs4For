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
                throw new InvalidOperationException("Cannot parse arbitrary NTFS files before initialization completed!");

            var components = new LinkedList<string>(path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));
            var references = new Stack<FileReference>();
            references.Push(RootFolder.GetReference());
            NtfsFile file = RootFolder;

            int symlinkHops = 0;

            while (components.Count > 0)
            {
                string entry = components.First!.Value;
                components.RemoveFirst();

                if (entry is "" or ".")
                    continue;

                if (entry == "..")
                {
                    if (references.Count <= 1)
                        throw new ArgumentException("Cannot do \"..\" when already at the root!");

                    references.Pop();
                    file = GetNtfsFile(references.Peek(), true);
                    continue;
                }

                if (!file.FileRecord.Flags.HasFlag(FileRecordFlags.IsDirectory))
                    throw new InvalidOperationException($"\"{entry}\" cannot be looked up inside a non-directory!");

                IndexParser parser = CreateIndexParser(file, "$I30");
                var indexEntry = parser.TryFindRootEntry(Encoding.Unicode.GetBytes(entry));
                if (indexEntry == null)
                    throw new FileNotFoundException($"Failed to find file/folder \"{entry}\"!");

                var reference = indexEntry.GetI30MftReference()!.Value;
                var resolved = GetNtfsFile(reference, true);

                // Check the entry we just found, not the 'file' from before descending.
                var reparse = resolved.NtfsAttributes.FirstOrDefault(x => x.Type == NtfsAttributeType.Reparse);
                if (reparse != null)
                {
                    var reparseData = reparse.Cast<ReparseAttribute>(true);
                    if (reparseData.TypeAndFlags.HasFlag(ReparseTypeAndFlags.SymbolicLink))
                    {
                        if (++symlinkHops > NtfsPolicyConfig.HopLimit)
                            throw new InvalidOperationException("Too many symbolic link redirections -- possible loop.");

                        var symlinkData = reparseData.Data as SymbolicLinkReparseData
                            ?? throw new InvalidOperationException($"\"{entry}\" has Reparse type SymbolicLink but Data is of another type!");

                        if (!symlinkData.Flags.HasFlag(SymbolicLinkReparseData.SymlinkFlags.IsRelative))
                            throw new NotSupportedException(
                                $"Only relative symbolic links are currently supported (\"{entry}\" is absolute).");

                        var targetParts = symlinkData.SubstituteName.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                        for (int i = targetParts.Length - 1; i >= 0; i--)
                            components.AddFirst(targetParts[i]);

                        continue;
                    }
                }

                if (references.Contains(reference))
                    throw new InvalidOperationException($"Detected circular folder structure involving \"{entry}\"!");

                references.Push(reference);
                file = resolved;
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