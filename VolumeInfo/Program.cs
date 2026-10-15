using System.Buffers.Binary;
using System.Numerics;
using Nternal;
using Nternal.Core;
using Nternal.Ntfs;

namespace VolumeInfo
{
    internal class Program
    {
        static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: VolumeInfo <source>");
                Console.WriteLine("  <source>  a drive letter on Windows (e.g. D:), or a device / image path (e.g. /dev/sdb1, disk.img)");
                return 1;
            }

            return PrintVolumeInfo(args[0]) ? 0 : 1;
        }

        /// <summary>
        /// Prints the geometry, metadata and cluster usage of an NTFS volume.
        /// </summary>
        public static bool PrintVolumeInfo(string source)
        {
            try
            {
                using var stream = new RawDriveStream(ResolveDevicePath(source));
                using var volume = new NtfsVolume(stream);
                volume.Mount(false);

                var boot = volume.BootSector;
                Console.WriteLine($"Volume            {source}");
                Console.WriteLine($"Label             {(volume.VolumeLabel.Length > 0 ? volume.VolumeLabel : "(none)")}");
                Console.WriteLine($"NTFS version      {volume.VolumeVersion}");
                Console.WriteLine($"Flags             {volume.VolumeFlags}");
                Console.WriteLine($"Serial number     {(uint)(boot.VolumeSerialNumber >> 16) & 0xFFFF:X4}-{(uint)boot.VolumeSerialNumber & 0xFFFF:X4} (full: {boot.VolumeSerialNumber:X16})");
                Console.WriteLine();

                Console.WriteLine($"Bytes per sector  {boot.BytesPerSector}");
                Console.WriteLine($"Bytes per cluster {boot.BytesPerCluster}");
                Console.WriteLine($"File record size  {boot.FileRecordSize}");
                Console.WriteLine($"Index buffer size {boot.IndexBufferSize}");
                Console.WriteLine($"Total size        {FormatSize(boot.TotalBytes)} ({boot.TotalClusters:N0} clusters)");
                Console.WriteLine($"$MFT at cluster   {boot.MftClusterNumber:N0}");
                Console.WriteLine($"$MFTMirr cluster  {boot.MftMirrClusterNumber:N0}");
                Console.WriteLine($"MFT records       {volume.GetMftRecordCount():N0}");
                Console.WriteLine();

                // Count allocated clusters straight from $Bitmap - one bit per cluster.
                long used = CountAllocatedClusters(volume, boot.TotalClusters);
                long free = boot.TotalClusters - used;
                Console.WriteLine($"Used              {FormatSize(used * boot.BytesPerCluster)} ({100.0 * used / boot.TotalClusters:F1}%)");
                Console.WriteLine($"Free              {FormatSize(free * boot.BytesPerCluster)}");

                var badRuns = volume.GetBadClusterRanges();
                long badClusters = badRuns.Sum(r => r.Length);
                Console.WriteLine($"Bad clusters      {badClusters:N0}{(badRuns.Length > 0 ? $" in {badRuns.Length} run(s)" : "")}");
                foreach (var run in badRuns)
                    Console.WriteLine($"  clusters {run.StartingCluster:N0} - {run.StartingCluster + run.Length - 1:N0}");

                return true;
            }

            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Failed to open {source}: {ex.Message}");
                Console.WriteLine("Make sure you are running this program as administrator / root!");
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read volume information: {ex.Message}");
            }

            return false;
        }

        private static long CountAllocatedClusters(NtfsVolume volume, long totalClusters)
        {
            using var bitmap = volume.GetKnownNtfsFile(KnownMftRecord.Bitmap).GetDataStream();

            const int BufferSize = 1048576;
            byte[] buffer = new byte[BufferSize];
            long bytesNeeded = (totalClusters + 7) / 8;
            long position = 0, used = 0;

            while (position < bytesNeeded)
            {
                int toRead = (int)Math.Min(BufferSize, bytesNeeded - position);
                bitmap.ReadExactly(position, buffer.AsSpan(0, toRead));

                int i = 0;
                for (; i + 8 <= toRead; i += 8)
                    used += BitOperations.PopCount(BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(i)));
                for (; i < toRead; i++)
                    used += BitOperations.PopCount(buffer[i]);

                position += toRead;
            }

            // The last byte may contain bits beyond the end of the volume - don't count those.
            int trailingBits = (int)(totalClusters % 8);
            if (trailingBits != 0)
                used -= BitOperations.PopCount((uint)(buffer[(int)((bytesNeeded - 1) % BufferSize)] >> trailingBits));

            return used;
        }

        private static string ResolveDevicePath(string source)
        {
            // "D" or "D:" -> raw volume on Windows; anything else is used as-is (device node or image file)
            if (OperatingSystem.IsWindows() && source.TrimEnd(':').Length == 1 && char.IsLetter(source[0]))
                return $@"\\.\{char.ToUpperInvariant(source[0])}:";

            return source;
        }

        private static string FormatSize(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes} B" : $"{size:F2} {units[unit]}";
        }
    }
}
