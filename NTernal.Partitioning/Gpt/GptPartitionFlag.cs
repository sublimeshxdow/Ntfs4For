using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Partitioning.Gpt
{
    [Flags]
    public enum GptPartitionFlags : ulong
    {
        None = 0,

        // ---- Universal (UEFI spec, any partition type) ----
        /// <summary>Partition is required for the platform to function; must not be removed/modified.</summary>
        RequiredPartition = 1UL << 0,
        /// <summary>Firmware must NOT produce an EFI_BLOCK_IO_PROTOCOL for this partition.</summary>
        NoBlockIoProtocol = 1UL << 1,
        /// <summary>
        /// Hint for legacy PC-AT BIOS bootloaders that this partition may be
        /// bootable. A conformant UEFI boot manager MUST ignore this bit.
        /// </summary>
        LegacyBiosBootable = 1UL << 2,

        // Bits 3-47: reserved by the UEFI spec, must be zero.

        // ---- Microsoft Basic Data Partition-specific (bits 48-63 range) ----
        // Only valid when PartitionTypeGuid is the Basic Data Partition GUID.

        /// <summary>Volume is read-only. Only meaningful for Basic Data Partitions.</summary>
        MsftReadOnly = 1UL << 60,
        /// <summary>Volume is hidden. Only meaningful for Basic Data Partitions.</summary>
        MsftHidden = 1UL << 62,
        /// <summary>OS must not auto-assign a drive letter. Only meaningful for Basic Data Partitions.</summary>
        MsftNoDriveLetter = 1UL << 63,
    }
}
