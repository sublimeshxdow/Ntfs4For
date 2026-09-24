using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Nternal.Core
{
    static class DiskInfo
    {
        const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        const int StorageAccessAlignmentProperty = 6;
        const int PropertyStandardQuery = 0;

        const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C; // needs read access
        const uint IOCTL_DISK_GET_PARTITION_INFO_EX = 0x00070048; // any access
        const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0; // any access

        [StructLayout(LayoutKind.Sequential)]
        struct STORAGE_PROPERTY_QUERY
        {
            public int PropertyId;
            public int QueryType;
            public byte AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct STORAGE_ACCESS_ALIGNMENT_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            public uint BytesPerCacheLine;
            public uint BytesOffsetForCacheAlignment;
            public uint BytesPerLogicalSector;
            public uint BytesPerPhysicalSector;
            public uint BytesOffsetForSectorAlignment;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share,
            IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle device, uint code,
            ref STORAGE_PROPERTY_QUERY inBuf, int inSize,
            out STORAGE_ACCESS_ALIGNMENT_DESCRIPTOR outBuf, int outSize,
            out int bytesReturned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle device, uint code,
            IntPtr inBuf, int inSize, byte[] outBuf, int outSize,
            out int bytesReturned, IntPtr overlapped);

        public static (uint Logical, uint Physical) GetSectorSizes(string devicePath)
        {
            using var handle = CreateFile(devicePath, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var query = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = StorageAccessAlignmentProperty,
                QueryType = PropertyStandardQuery
            };

            if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                    ref query, Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(),
                    out var desc, Marshal.SizeOf<STORAGE_ACCESS_ALIGNMENT_DESCRIPTOR>(),
                    out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return (desc.BytesPerLogicalSector, desc.BytesPerPhysicalSector);
        }

        /// <summary>Total length in bytes of a disk, partition, or volume.</summary>
        /// <param name="path">e.g. \\.\PhysicalDrive0, \\.\C:, \\.\Harddisk0Partition2</param>
        public static long GetLength(string path)
        {
            // Zero access is enough for the fallbacks and doesn't need admin.
            // If the caller has admin, opening with GENERIC_READ (0x80000000)
            // lets the preferred IOCTL succeed first.
            using var handle = CreateFile(path, 0, 3 /* share R|W */, IntPtr.Zero,
                3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open {path}");
            return GetLength(handle);
        }

        /// <summary>Use with an existing FileStream: GetLength(stream.SafeFileHandle).</summary>
        public static long GetLength(SafeFileHandle handle)
        {
            var buf = new byte[256];

            // 1. GET_LENGTH_INFORMATION { LONGLONG Length; }
            //    Works for disks, partitions, and dynamic/spanned volumes.
            if (Query(handle, IOCTL_DISK_GET_LENGTH_INFO, buf))
                return BitConverter.ToInt64(buf, 0);
            int firstError = Marshal.GetLastWin32Error();

            // 2. PARTITION_INFORMATION_EX: PartitionLength is at offset 16.
            //    On a physical drive this describes partition 0, i.e. the whole disk.
            if (Query(handle, IOCTL_DISK_GET_PARTITION_INFO_EX, buf))
                return BitConverter.ToInt64(buf, 16);

            // 3. DISK_GEOMETRY_EX: DiskSize follows the 24-byte DISK_GEOMETRY.
            if (Query(handle, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, buf))
                return BitConverter.ToInt64(buf, 24);

            throw new Win32Exception(firstError, "Device did not report its length.");
        }

        static bool Query(SafeFileHandle h, uint code, byte[] buf) =>
            DeviceIoControl(h, code, IntPtr.Zero, 0, buf, buf.Length, out _, IntPtr.Zero);
    }
}
