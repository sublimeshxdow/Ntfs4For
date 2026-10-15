using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nternal.Core.Platform
{
    [SupportedOSPlatform("linux")]
    static class LinuxDiskInfo
    {
        const uint BLKSSZGET = 0x00001268; // _IO(0x12, 104)           -> int:   logical sector size
        const uint BLKPBSZGET = 0x0000127B; // _IO(0x12, 123)           -> uint:  physical sector size
        const uint BLKGETSIZE64 = 0x80081272; // _IOR(0x12, 114, size_t)  -> u64:   size in bytes

        const int ENOTTY = 25; // ioctl not supported by this fd (e.g. a regular file)
        
        public const uint DefaultSectorSize = 512;

        [DllImport("libc", SetLastError = true)]
        static extern int ioctl(int fd, nuint request, out int value);

        [DllImport("libc", SetLastError = true)]
        static extern int ioctl(int fd, nuint request, out uint value);

        [DllImport("libc", SetLastError = true)]
        static extern int ioctl(int fd, nuint request, out ulong value);

        static SafeFileHandle Open(string path) =>
            File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        public static (uint Logical, uint Physical) GetSectorSizes(string devicePath)
        {
            using var handle = Open(devicePath);
            return GetSectorSizes(handle);
        }

        public static (uint Logical, uint Physical) GetSectorSizes(SafeFileHandle handle)
        {
            return WithFd(handle, fd =>
            {
                if (ioctl(fd, BLKSSZGET, out int logical) != 0)
                {
                    int errno = Marshal.GetLastPInvokeError();
                    if (errno == ENOTTY)

                        return (DefaultSectorSize, DefaultSectorSize);
                    throw new Win32Exception(errno, "BLKSSZGET failed!");
                }

                uint physical = ioctl(fd, BLKPBSZGET, out uint p) == 0 && p != 0 ? p : (uint)logical;
                return ((uint)logical, physical);
            });
        }

        public static long GetLength(string path)
        {
            using var handle = Open(path);
            return GetLength(handle);
        }

        public static long GetLength(SafeFileHandle handle)
        {
            return WithFd(handle, fd =>
            {
                if (ioctl(fd, BLKGETSIZE64, out ulong size) == 0)
                    return checked((long)size);

                int errno = Marshal.GetLastPInvokeError();
                if (errno != ENOTTY)
                    throw new Win32Exception(errno, "BLKGETSIZE64 failed!");

                // Not a block device: a regular image file has a normal file length.
                return RandomAccess.GetLength(handle);
            });
        }

        // Keeps the handle alive for the duration of the ioctl
        static T WithFd<T>(SafeFileHandle handle, Func<int, T> action)
        {
            bool added = false;
            try
            {
                handle.DangerousAddRef(ref added);
                return action((int)handle.DangerousGetHandle());
            }

            finally
            {
                if (added)
                    handle.DangerousRelease();
            }
        }
    }
}