using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static Nternal.Core.Platform.Win32Api;

namespace Nternal.Core.Platform;

internal static class Win32DiskInfo
{
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

        if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, ref query, Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(),
                out var desc, Marshal.SizeOf<STORAGE_ACCESS_ALIGNMENT_DESCRIPTOR>(), out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return (desc.BytesPerLogicalSector, desc.BytesPerPhysicalSector);
    }

    public static long GetLength(string path)
    {
        using var handle = CreateFile(path, 0, (uint)FileShare.ReadWrite, IntPtr.Zero, (uint)FileMode.Open, 0,
            IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to open \"{path}\"!");

        return GetLength(handle);
    }

    public static long GetLength(SafeFileHandle handle)
    {
        var buf = new byte[256];
        if (Query(handle, IOCTL_DISK_GET_LENGTH_INFO, buf))
            return BitConverter.ToInt64(buf, 0);

        var error = Marshal.GetLastWin32Error();
        if (Query(handle, IOCTL_DISK_GET_PARTITION_INFO_EX, buf))
            return BitConverter.ToInt64(buf, 16);

        if (Query(handle, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, buf))
            return BitConverter.ToInt64(buf, 24);

        throw new Win32Exception(error, "Failed to query device length!");
    }

    private static bool Query(SafeFileHandle h, uint code, byte[] buf)
    {
        return DeviceIoControl(h, code, IntPtr.Zero, 0, buf, buf.Length, out _, IntPtr.Zero);
    }
}