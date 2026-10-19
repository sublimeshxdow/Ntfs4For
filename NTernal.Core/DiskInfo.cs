using Microsoft.Win32.SafeHandles;
using Nternal.Core.Platform;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Core
{
    static class DiskInfo
    {
        /// <summary>
        /// Queries the logical and physical sector sizes.
        /// </summary>
        /// <param name="path">The path to the object the sector sizes should be retrieved from.</param>
        /// <returns>Returns the logical and physical sector sizes.</returns>
        /// <exception cref="PlatformNotSupportedException">On any other platform than Windows/Linux.</exception>
        public static (uint Logical, uint Physical) GetSectorSizes(string path)
        {
            if (OperatingSystem.IsWindows())
                return Win32DiskInfo.GetSectorSizes(path);
            else if (OperatingSystem.IsLinux())
                return LinuxDiskInfo.GetSectorSizes(path);
            else
                throw new PlatformNotSupportedException();
        }

        /// <summary>
        /// Queries the amount of bytes the specified object contains.
        /// </summary>
        /// <param name="path">The path of the object the length should be retrieved from.</param>
        /// <returns>Returns the amount of bytes the specified object contains.</returns>
        /// <exception cref="PlatformNotSupportedException">On any other platform than Windows/Linux.</exception>
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
