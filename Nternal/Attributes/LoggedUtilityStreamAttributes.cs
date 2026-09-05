using Nternal.Extensions;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nternal.Attributes
{
    public enum EfsVersion : int
    {
        DesxRsa = 1,
        Desx3DesAes256Rsa = 2,
        Desx3DesAes256_Wrapped = 3
    }

    public enum EfsKeyFlag
    {
        RsaWrapped = 0,
        AesWrapped = 1,
    }

    public class LoggedUtilityStream : NtfsAttribute
    {
        // ---- Outer header offsets (MS-EFSR 2.2.2.1 -- spec-documented) ----
        private const int LengthOffset = 0x00;
        private const int VersionOffset = 0x08;
        private const int EfsIdOffset = 0x10;   // 16 bytes
        private const int EfsHashOffset = 0x20; // 16 bytes
        private const int DdfOffsetOffset = 0x40;
        private const int DrfOffsetOffset = 0x44;

        // ---- Key List Entry offsets, relative to entry start
        // (MS-EFSR 2.2.2.1.2 -- spec-documented) ----
        private const int EntryPkiHeaderOffsetOffset = 0x04;
        private const int EntryFekLengthOffset = 0x08;
        private const int EntryFekOffsetOffset = 0x0C;
        private const int EntryFlagsOffset = 0x10;

        // ---- Public Key Information sub-header, relative to its own start
        // (UNDOCUMENTED -- reverse-engineered from real samples). The field
        // at PkiHeaderSidOffsetOffset (rel. 0x04) gives the SID's offset from
        // the header start -- but a SID is not always present: a value of 0
        // there is a sentinel meaning "no SID", observed on a real DRF
        // (recovery agent) entry, since a DRA isn't a normal Windows user
        // account. The field at rel. 0x00 is of unclear exact semantics and
        // deliberately not relied on -- see ParseKeyEntry. ----
        private const int PkiHeaderSidOffsetOffset = 0x04;
        private const int PkiHeaderSize = 28; // fixed size of the PKI sub-header itself
        private const int ThumbprintSize = 20; // SHA-1

        // Fixed 20-byte run of fields sitting between the end of the SID (or
        // the end of the PKI header, if no SID) and the start of the actual
        // thumbprint. Meaning not decoded.
        private const int PostSidReservedSize = 20;

        public class KeyEntry
        {
            public EfsKeyFlag Flags { get; set; }
            public byte[] EncryptedFek { get; set; }

            public string Sid { get; set; }
            public byte[] Thumbprint { get; set; }
            public string ContainerGuid { get; set; }
            public string Provider { get; set; }
            public string UserName { get; set; }
        }

        public EfsVersion Version { get; set; }
        public Guid EfsId { get; set; }
        public byte[] EfsHash { get; set; }

        public List<KeyEntry> DDFKeys { get; set; }
        public List<KeyEntry> DRFKeys { get; set; }

        public override void ParseFromAccessor()
        {
            Span<byte> buffer = new byte[Accessor.Length];
            Accessor.ReadExactly(0, buffer);

            int length = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(LengthOffset, 4));
            if (buffer.Length < length)
                throw new InvalidDataException(
                    $"Buffer length is less than expected length: {buffer.Length} < {length}");

            Version = (EfsVersion)BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(VersionOffset, 4));
            EfsId = new Guid(buffer.Slice(EfsIdOffset, 16));
            EfsHash = buffer.Slice(EfsHashOffset, 16).ToArray();

            int ddfOffset = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(DdfOffsetOffset, 4));
            int drfOffset = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(DrfOffsetOffset, 4));

            DDFKeys = ddfOffset != 0 ? ParseKeyList(buffer, ddfOffset) : new List<KeyEntry>();
            DRFKeys = drfOffset != 0 ? ParseKeyList(buffer, drfOffset) : new List<KeyEntry>();
        }

        private static List<KeyEntry> ParseKeyList(Span<byte> buffer, int listOffset)
        {
            int count = buffer.ReadInt32(listOffset);
            int currentOffset = listOffset + 4;

            var keys = new List<KeyEntry>(count);
            for (int i = 0; i < count; i++)
            {
                Span<byte> entry = buffer.Slice(currentOffset, buffer.ReadInt32(currentOffset));
                currentOffset += entry.Length;

                keys.Add(ParseKeyEntry(entry));
            }

            return keys;
        }

        private static KeyEntry ParseKeyEntry(Span<byte> entry)
        {
            int pkiHeaderOffset = entry.ReadInt32(EntryPkiHeaderOffsetOffset);
            int fekLength = entry.ReadInt32(EntryFekLengthOffset);
            int fekOffset = entry.ReadInt32(EntryFekOffsetOffset);

            byte[] fek = entry.Slice(fekOffset, fekLength).ToArray();

            // A SID is only present if the header's SID-offset field is
            // non-zero. When present, it sits right after the fixed-size PKI
            // header; when absent (DRF/recovery-agent entries), the fixed
            // 20-byte reserved gap follows the header directly.
            int sidOffsetField = entry.ReadInt32(pkiHeaderOffset + PkiHeaderSidOffsetOffset);
            bool hasSid = sidOffsetField != 0;

            string sid = null;
            int afterHeaderOrSid;
            if (hasSid)
            {
                int sidOffset = pkiHeaderOffset + sidOffsetField;
                ReadOnlySpan<byte> sidSpan = entry.Slice(sidOffset, entry.Length - sidOffset);
                int sidLength = SidLength(sidSpan, 0);
                sid = FormatSid(sidSpan);
                afterHeaderOrSid = sidOffset + sidLength;
            }
            else
            {
                afterHeaderOrSid = pkiHeaderOffset + PkiHeaderSize;
            }

            int thumbOffset = afterHeaderOrSid + PostSidReservedSize;
            byte[] thumbprint = entry.Slice(thumbOffset, ThumbprintSize).ToArray();
            ReadOnlySpan<byte> stringData = entry.Slice(thumbOffset + ThumbprintSize);

            // String-field mapping is conditional on SID presence, verified
            // against one real sample of each: entries WITH a SID (DDF) carry
            // [ContainerGuid, ProviderName, UserName]; entries WITHOUT one
            // (DRF) carry just [UserName] -- no container/provider, since a
            // DRA cert isn't necessarily tied to a legacy CSP key container
            // the same way a normal DDF cert is.
            string containerGuid = null, provider = null, userName = null;
            if (hasSid)
            {
                containerGuid = ReadZeroTerminatedString(stringData, out int read);
                stringData = stringData.Slice(read);
                provider = ReadZeroTerminatedString(stringData, out read);
                stringData = stringData.Slice(read);
                userName = ReadZeroTerminatedString(stringData, out _);
            }
            else
            {
                userName = ReadZeroTerminatedString(stringData, out _);
            }

            return new KeyEntry
            {
                Flags = (EfsKeyFlag)entry.ReadInt32(EntryFlagsOffset),
                EncryptedFek = fek,
                Sid = sid,
                Thumbprint = thumbprint,
                ContainerGuid = containerGuid,
                Provider = provider,
                UserName = userName,
            };
        }

        private static int SidLength(ReadOnlySpan<byte> data, int sidOffset) =>
            8 + data[sidOffset + 1] * 4;

        private static string FormatSid(ReadOnlySpan<byte> data)
        {
            byte revision = data[0];
            byte subCount = data[1];
            long authority = 0;
            for (int i = 0; i < 6; i++) authority = (authority << 8) | data[2 + i];

            var sb = new StringBuilder();
            sb.Append('S').Append('-').Append(revision).Append('-').Append(authority);
            int p = 8;
            for (int i = 0; i < subCount; i++)
            {
                sb.Append('-').Append(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p, 4)));
                p += 4;
            }
            return sb.ToString();
        }

        private static string ReadZeroTerminatedString(ReadOnlySpan<byte> data, out int read)
        {
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0 && data[i + 1] == 0)
                {
                    read = i + 2;
                    return Encoding.Unicode.GetString(data.Slice(0, i));
                }
            }
            throw new ArgumentException("String is not zero-terminated");
        }
    }
}