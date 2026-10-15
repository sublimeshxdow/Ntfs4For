using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Nternal.Core.Platform
{
    internal static class Win32Api
    {
        internal const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        internal const int StorageAccessAlignmentProperty = 6;
        internal const int PropertyStandardQuery = 0;

        internal const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C; // needs read access
        internal const uint IOCTL_DISK_GET_PARTITION_INFO_EX = 0x00070048; // any access
        internal const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0; // any access

        [StructLayout(LayoutKind.Sequential)]
        internal struct STORAGE_PROPERTY_QUERY
        {
            public int PropertyId;
            public int QueryType;
            public byte AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct STORAGE_ACCESS_ALIGNMENT_DESCRIPTOR
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
        internal static extern SafeFileHandle CreateFile(string name, uint access, uint share,
            IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool DeviceIoControl(SafeFileHandle device, uint code,
            ref STORAGE_PROPERTY_QUERY inBuf, int inSize,
            out STORAGE_ACCESS_ALIGNMENT_DESCRIPTOR outBuf, int outSize,
            out int bytesReturned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool DeviceIoControl(SafeFileHandle device, uint code,
            IntPtr inBuf, int inSize, byte[] outBuf, int outSize,
            out int bytesReturned, IntPtr overlapped);
    }
}
