using Nternal.Core;
using Nternal.Ntfs;

namespace MftExporter;

internal class Program
{
    private const string Usage =
        """
        Usage: MftExporter [options] [drive] [output-directory]

        Dumps the $MFT of an NTFS volume to a file named "$Mft" in the output directory.

        Arguments:
          drive                 Drive letter, e.g. C, C: or C:\     (default: C)
          output-directory      Directory to write $Mft into         (default: your desktop)

        Options:
          -d, --drive <drive>   Same as the drive argument
          -o, --output <dir>    Same as the output-directory argument
          -h, --help            Show this help

        Examples:
          MftExporter D
          MftExporter D: "C:\Dumps"
          MftExporter --drive E --output "%USERPROFILE%\Dumps"
        """;

    private static int Main(string[] args)
    {
        var driveLetter = 'C';
        var outputPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var positional = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    Console.WriteLine(Usage);
                    return 0;

                case "-d" or "--drive":
                    if (!TryGetValue(args, ref i, out var drive) || !TryParseDrive(drive, out driveLetter))
                        return Fail($"Invalid or missing drive for {arg}.");
                    break;

                case "-o" or "--output":
                    if (!TryGetValue(args, ref i, out var output))
                        return Fail($"Missing directory for {arg}.");
                    outputPath = output;
                    break;

                default:
                    if (arg.StartsWith('-') && arg.Length > 1)
                        return Fail($"Unknown option \"{arg}\".");
                    positional.Add(arg);
                    break;
            }
        }

        if (positional.Count > 2)
            return Fail("Too many arguments.");
        if (positional.Count >= 1 && !TryParseDrive(positional[0], out driveLetter))
            return Fail($"\"{positional[0]}\" is not a valid drive letter.");
        if (positional.Count == 2)
            outputPath = positional[1];

        outputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        return ExportMft(driveLetter, outputPath) ? 0 : 1;
    }

    private static bool TryGetValue(string[] args, ref int i, out string value)
    {
        if (i + 1 >= args.Length)
        {
            value = "";
            return false;
        }

        value = args[++i];
        return true;
    }

    private static bool TryParseDrive(string value, out char driveLetter)
    {
        var trimmed = value.TrimEnd('\\', '/').TrimEnd(':');
        if (trimmed.Length == 1 && char.IsAsciiLetter(trimmed[0]))
        {
            driveLetter = char.ToUpperInvariant(trimmed[0]);
            return true;
        }

        driveLetter = default;
        return false;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Use --help for usage.");
        return 2;
    }

    /// <summary>
    ///     Exports the master file table of the drive with the given drive-letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter, e.g. 'C'.</param>
    /// <param name="outputPath">The directory to write "$Mft" into; created if missing.</param>
    /// <returns><see langword="true" /> on success.</returns>
    public static bool ExportMft(char driveLetter, string outputPath)
    {
        var driveName = $"{driveLetter}:";

        try
        {
            var drive = new DriveInfo(driveName);
            var fileSystem = drive.DriveFormat;
            if (!string.Equals(fileSystem, "NTFS", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"Drive {driveName} is of filesystem {fileSystem} - only NTFS is supported!");
        }

        catch (Exception ex)
        {
            Console.WriteLine($"Failed to retrieve file system of drive {driveName}: {ex.Message}");
            return false;
        }

        var outputFilePath = Path.Combine(outputPath, "$Mft");
        var outputCreated = false;

        try
        {
            // Mount volume
            using var stream = new RawDriveStream(@$"\\.\{driveName}");
            using var volume = new NtfsVolume(stream);
            volume.Mount(false);

            // Retrieve mft-file
            var mft = volume.GetNtfsFile(volume.GetKnownMftReference(KnownMftRecord.Mft));
            using var dataStream = mft.GetDataStream();

            // Read $Mft
            const int BufferSize = 1048576;
            var buffer = new byte[BufferSize];

            // Start writing to output
            Directory.CreateDirectory(outputPath);
            long written;
            using (FileStream outputFile =
                   new(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            {
                outputCreated = true;

                long position = 0;
                var length = dataStream.Length;

                while (position < length)
                {
                    var read = dataStream.Read(position, buffer);
                    if (read <= 0)
                        throw new IOException("Unexpected end of MFT stream.");

                    outputFile.Write(buffer, 0, read);
                    position += read;
                }

                written = outputFile.Length;
            }

            Console.WriteLine(
                $"Successfully dumped the master file table of drive {driveName} to {outputFilePath} [{written} bytes].");
            return true;
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

        if (outputCreated)
            try
            {
                File.Delete(outputFilePath);
            }
            catch
            {
            }

        return false;
    }
}