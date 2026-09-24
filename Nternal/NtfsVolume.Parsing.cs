using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Nternal.Attributes;
using Nternal.Core.Streams;
using Nternal.Extensions;
using Nternal.Helpers;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Hashing;
using Nternal.Ntfs.Index;
using Nternal.Ntfs.Security;
using Nternal.Structs;

namespace Nternal
{
    public partial class NtfsVolume
    {
        public RawFileRecord GetRawRecordData(FileReference reference, bool isAttributeLookup = false,
            bool compareSequence = true, IDataStream? mft = null)
        {
            if (_hasParsedMftYet)
            {
                if (MftBitmap == null)
                {
                    MftBitmap = MftFile!.GetAttributeData<BitmapAttribute>("");
                    if (MftBitmap == null)
                        throw new InvalidOperationException(
                            $"$Mft does not seem to have an unnamed bitmap-attribute - corruption detected.");
                }

                if (!MftBitmap.IsInRange((long)reference.FileRecordId))
                    throw new Exception($"Attempted to parse an $Mft-Record! outside the valid range!");

                if (!MftBitmap.IsSet((long)reference.FileRecordId))
                    throw new Exception($"Attempted to parse an unused $Mft-Record!");
            }

            var mftStream = mft ?? MftFile!.GetAttributeData<DataAttribute>(NtfsAttributeType.Data, "")?.GetStream();
            if (mftStream == null)
                throw new InvalidOperationException(
                    $"$Mft does not seem to have an unnamed data-attribute - corruption detected.");

            var fileRecordSize = BootSector.FileRecordSize;
            Span<byte> fileRecord = fileRecordSize <= 4096 ? stackalloc byte[fileRecordSize] : new byte[fileRecordSize];
            mftStream.ReadExactly((long)reference.FileRecordId * fileRecordSize, fileRecord);

            FixupHelper.FixupRecord(fileRecord);

            var rawRecord = new RawFileRecord(fileRecord);
            if (rawRecord.MagicNumber != GenericFileRecord.ExpectedMagicNumber)
                throw new InvalidDataException($"FileRecord referenced by {reference} has an invalid magic number ({rawRecord.MagicNumber}, " +
                    $"expected \"{GenericFileRecord.ExpectedMagicNumber}\")!");

            if (compareSequence && rawRecord.SequenceNumber != reference.SequenceNumber)
            {
                string msg = $"Attempted to parse record for file-reference {reference}, but that doesn't match with the record's sequence-number {rawRecord.SequenceNumber}!";
                if (isAttributeLookup)
                    msg += "\nThis happened during an attribute lookup. Ensure the volume is not corrupted, and that the volume was opened locking!";

                throw new ArgumentException(msg);
            }

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

        public FileReference GetKnownMftReference(KnownMftRecord record)
        {
            return new FileReference((ulong)record, (ushort)Math.Max((ulong)record, 1));
        }

        public NtfsFile GetKnownNtfsFile(KnownMftRecord record)
        {
            return GetNtfsFile(GetKnownMftReference(record), true);
        }

        public NtfsFile GetNtfsFile(FileReference reference, bool compareSequence = true)
        {
            var rawRecord = GetRawRecordData(reference, isAttributeLookup: false, compareSequence: compareSequence);
            return new NtfsFile(rawRecord, _provider, this, true);
        }

        public long GetMftRecordCount()
        {
            return MftFile!.GetDataStream().Length / BootSector.FileRecordSize;
        }

        public string GetPrimaryAbsolutePath(FileReference reference)
        {
            FileReference root = GetKnownMftReference(KnownMftRecord.Root);
            FileReference currentReference = reference;
            StringBuilder sb = new(256);

            while (currentReference != root)
            {
                NtfsFile? file;

                try
                {
                    file = GetNtfsFile(currentReference);
                }

                catch (Exception ex)
                {
                    sb.Insert(0, "??");
                    break;
                }

                var fileNameAttr = file.GetRequiredAttributeData<FileNameAttribute>(NtfsAttributeType.FileName, "");
                currentReference = fileNameAttr.FileNameData.ParentFolder;
                sb.Insert(0, $"/{fileNameAttr.FileNameData.Name}");
            }

            return sb.ToString();
        }

        public NtfsFile GetNtfsFile(string path)
        {
            if (RootFolder == null)
                throw new InvalidOperationException(
                    "Cannot parse arbitrary NTFS files before initialization completed!");

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
                            throw new InvalidOperationException(
                                "Too many symbolic link redirections -- possible loop.");

                        var symlinkData = reparseData.Data as SymbolicLinkReparseData
                                          ?? throw new InvalidOperationException(
                                              $"\"{entry}\" has Reparse type SymbolicLink but Data is of another type!");

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

        public SecurityDescriptor? GetSecurityDescriptor(NtfsFile file)
        {
            var descriptor = file.GetAttributeData<SecurityDescriptorAttribute>("");
            if (descriptor != null)
                return descriptor.Descriptor;

            var index = file.GetAttributeData<StandardInformationAttribute>()?.SecurityId;
            return index != null ? LookupSecurityDescriptorByIndex(index.Value) : null;
        }

        private SecurityDescriptor? LookupSecurityDescriptorByIndex(uint securityId)
        {
            Span<byte> lookupKey = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(lookupKey, securityId);

            var index = CreateIndexParser(SecureFile, "$SII");
            var entry = index.TryFindRootEntry(lookupKey);
            if (entry == null)
                return null;

            long offset = entry.GetData().ReadInt64(0x08);
            int size = entry.GetData().ReadInt32(0x10);
            const int headerOffset = 20;

            var sdsStream = SecureFile.GetDataStream("$SDS");
            Span<byte> sdsEntry = size <= 256 ? stackalloc byte[size] : new byte[size];
            sdsStream.ReadExactly(offset, sdsEntry);

            // Let's see if the hash matches
            Trace.Assert(sdsEntry.ReadUInt32(0x00) == SecurityHashing.HashSecurityDescriptor(sdsEntry[headerOffset..]));

            uint writtenSecurityId = sdsEntry.ReadUInt32(0x04);
            long writtenOffset = sdsEntry.ReadInt64(0x08);
            int writtenSize = sdsEntry.ReadInt32(0x10);

            if (NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.ValidateSdh)
                && (securityId != writtenSecurityId || offset != writtenOffset || size != writtenSize))
                throw new InvalidDataException(
                    $"Invalid $Secure:$SDS stream - offset/length mismatch of entry with securityId {securityId}" +
                    $"(IndexKeyOffset={offset}, IndexKeySize={size}, SdsEntryOffset={writtenOffset}, SdsEntrySize={writtenSize})!");

            return new SecurityDescriptor(sdsEntry[headerOffset..]);
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

        public IndexParser CreateIndexParser(NtfsFile file, string index) =>
            new IndexParser(file, index, CollationHandler, BootSector);
    }
}