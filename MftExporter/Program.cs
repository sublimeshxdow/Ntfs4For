using Nternal;
using Nternal.Core;
using Nternal.Ntfs;

namespace MftExporter
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            ExportMft('D', desktop);
        }

        /// <summary>
        /// Exports the master file table of the drive with the given drive-letter.
        /// </summary>
        /// <param name="driveLetter"></param>
        public static void ExportMft(char driveLetter, string outputPath)
        {
            string driveName = $"{driveLetter}:";

            try
            {
                var drive = new DriveInfo(driveName);
                string fileSystem = drive.DriveFormat;
                if (!string.Equals(fileSystem, "NTFS", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Drive {driveName} is of filesystem {fileSystem} - only NTFS is supported!");
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Failed to retrieve file system of drive {driveName}: {ex.Message}");
                return;
            }

            try
            {
                // Mount volume
                using var stream = new RawDriveStream(@$"\\.\{driveName}");
                using var volume = new NtfsVolume(stream);
                volume.Mount(false);

                // Retrieve mft-file
                var mft = volume.GetNtfsFile(
                    volume.GetKnownMftReference(KnownMftRecord.Mft));

                using var dataStream = mft.GetDataStream();

                // Read $Mft
                const int BufferSize = 1048576;
                byte[] buffer = new byte[BufferSize];

                // Start writing to output
                Directory.CreateDirectory(outputPath);
                string outputFilePath = Path.Combine(outputPath, "$Mft");
                using FileStream outputFile = new(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize);

                long position = 0;
                long length = dataStream.Length;

                while (position < length)
                {
                    int read = dataStream.Read(position, buffer);
                    if (read <= 0)
                        throw new IOException("Unexpected end of MFT stream.");

                    outputFile.Write(buffer, 0, read);
                    position += read;
                }

                Console.WriteLine($"Successfully dumped the master file table of drive {driveName} to {outputFilePath} [{outputFile.Length} bytes].");
            }

            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Failed to dump the master file table: {ex.Message}");
                Console.WriteLine("Make sure you are running this program as administrator!");
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Failed to dump the master file table: {ex.Message}");
            }
        }
    }
}
