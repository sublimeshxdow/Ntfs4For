using Microsoft.Win32.SafeHandles;
using Nternal.Core.Platform;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Core
{
    static class DiskInfo
    {
        public static (uint Logical, uint Physical) GetSectorSizes(string path)
        {
            if (OperatingSystem.IsWindows())
                return Win32DiskInfo.GetSectorSizes(path);
            else if (OperatingSystem.IsLinux())
                return LinuxDiskInfo.GetSectorSizes(path);
            else
                throw new PlatformNotSupportedException();
        }

        public static long GetLength(string path)
        {
            if (OperatingSystem.IsWindows())
                return Win32DiskInfo.GetLength(path);
            else if (OperatingSystem.IsLinux())
                return LinuxDiskInfo.GetLength(path);
            else
                throw new PlatformNotSupportedException();
        }
    }
}
