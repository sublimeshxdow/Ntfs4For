using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Partitioning.Gpt
{
    /// <summary>
    /// This enum represents a non-exhaustive list of most (known) flags a GPT partitions can have.
    /// </summary>
    [Flags]
    public enum GptPartitionFlags : ulong
    {
        None = 0,

        // ---------------------------------------------------------------
        //         UEFI standard attributes (all partition types)
        // ---------------------------------------------------------------

        /// <summary>The platform needs this partition to function, so it shouldn’t be deleted or modified.</summary>
        RequiredPartition = 1UL << 0,

        /// <summary>The EFI firmware should not create a Block IO protocol for this partition.</summary>
        NoBlockIoProtocol = 1UL << 1,

        /// <summary>The equivalent to the MBR “active” flag, used by legacy/BIOS boot loaders.</summary>
        LegacyBiosBootable = 1UL << 2,

        // ---------------------------------------------------------------
        // Microsoft Basic Data partition
        // Type GUID: EBD0A0A2-B9E5-4433-87C0-68B6B72699C7 (winioctl.h)
        // ---------------------------------------------------------------

        /// <summary>Service partition.</summary>
        MsBasicDataService = 1UL << 57,

        /// <summary>Direct access (DAX) volume.</summary>
        MsBasicDataDax = 1UL << 58,

        /// <summary>Volume is offline.</summary>
        MsBasicDataOffline = 1UL << 59,

        /// <summary>Read-only volume.</summary>
        MsBasicDataReadOnly = 1UL << 60,

        /// <summary>Shadow copy of another partition.</summary>
        MsBasicDataShadowCopy = 1UL << 61,

        /// <summary>Hidden volume.</summary>
        MsBasicDataHidden = 1UL << 62,

        /// <summary>Do not assign a drive letter.</summary>
        MsBasicDataNoDriveLetter = 1UL << 63,

        // ---------------------------------------------------------------
        //      Linux / systemd Discoverable Partitions Specification
        // ---------------------------------------------------------------

        /// <summary>Grow the file system to fill the partition.</summary>
        SystemdGrowFs = 1UL << 59,

        /// <summary>Mount read-only.</summary>
        SystemdReadOnly = 1UL << 60,

        /// <summary>Do not auto-mount / auto-activate.</summary>
        SystemdNoAuto = 1UL << 63
    }
}
