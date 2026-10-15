using Nternal;
using Nternal.Attributes;
using Nternal.Core;
using Nternal.Ntfs;

namespace FileExtractor
{
    internal class Program
    {
        static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("Usage: FileExtractor <source> <path[:stream]> <output>");
                Console.WriteLine("  <source>         a drive letter on Windows (e.g. C:), or a device / image path");
                Console.WriteLine("  <path[:stream]>  file on the volume, e.g. /Windows/System32/config/SAM");
                Console.WriteLine("                   append :name to read an alternate data stream, e.g. /file.exe:Zone.Identifier");
                Console.WriteLine("  <output>         where to write the extracted copy");
                Console.WriteLine();
                Console.WriteLine("Reading the raw volume bypasses file locks, so this also works on files that are in use.");
                return 1;
            }

            return ExtractFile(args[0], args[1], args[2]) ? 0 : 1;
        }

        /// <summary>
        /// Copies a file (or one of its named data streams) from an NTFS volume to a local file.
        /// Compressed and sparse files are expanded transparently by the library.
        /// </summary>
        public static bool ExtractFile(string source, string pathOnVolume, string outputPath)
        {
            var (path, streamName) = SplitStreamName(pathOnVolume);

            try
            {
                using var stream = new RawDriveStream(ResolveDevicePath(source));
                using var volume = new NtfsVolume(stream);
                volume.Mount(false);

                var file = volume.GetNtfsFile(path);
                if (file.RecordHeader.Flags.HasFlag(Nternal.Structs.FileRecordFlags.IsDirectory) && streamName.Length == 0)
                    throw new ArgumentException($"\"{path}\" is a directory - only files can be extracted.");

                using var dataStream = file.GetDataStream(streamName);

                const int BufferSize = 1048576;
                byte[] buffer = new byte[BufferSize];

                string? outputFolder = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (outputFolder != null)
                    Directory.CreateDirectory(outputFolder);

                long length = dataStream.Length;
                using (FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                {
                    long position = 0;
                    while (position < length)
                    {
                        int toRead = (int)Math.Min(BufferSize, length - position);
                        int read = dataStream.Read(position, buffer.AsSpan(0, toRead));
                        if (read <= 0)
                            throw new IOException($"Unexpected end of stream at offset {position} of {length}.");

                        output.Write(buffer, 0, read);
                        position += read;
                    }
                }

                // Carry the original timestamps over, so the copy looks like the source.
                var info = file.GetAttributeData<StandardInformationAttribute>();
                if (info != null)
                {
                    File.SetCreationTimeUtc(outputPath, info.FileCreation);
                    File.SetLastWriteTimeUtc(outputPath, info.FileAltered);
                    File.SetLastAccessTimeUtc(outputPath, info.FileRead);
                }

                string what = streamName.Length > 0 ? $"stream \"{streamName}\" of {path}" : path;
                Console.WriteLine($"Extracted {what} to {outputPath} [{length:N0} bytes].");
                return true;
            }

            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Failed to open {source}: {ex.Message}");
                Console.WriteLine("Make sure you are running this program as administrator / root!");
            }

            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"Not found: {ex.Message}");
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Failed to extract {pathOnVolume}: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// "/folder/file.txt:Zone.Identifier" -> ("/folder/file.txt", "Zone.Identifier").
        /// Only the last path component is checked, so drive-style prefixes like "C:" don't confuse it.
        /// </summary>
        private static (string Path, string Stream) SplitStreamName(string pathOnVolume)
        {
            string path = pathOnVolume.Replace('\\', '/');

            // Allow "C:/Windows/..." - the drive letter is already given by <source>
            if (path.Length >= 2 && path[1] == ':' && char.IsLetter(path[0]))
                path = path[2..];

            int lastSlash = path.LastIndexOf('/');
            int colon = path.IndexOf(':', lastSlash + 1);
            if (colon < 0)
                return (path, "");

            // "file:stream:$DATA" is the fully qualified form - strip the type
            string stream = path[(colon + 1)..];
            if (stream.EndsWith(":$DATA", StringComparison.OrdinalIgnoreCase))
                stream = stream[..^":$DATA".Length];

            return (path[..colon], stream);
        }

        private static string ResolveDevicePath(string source)
        {
            if (OperatingSystem.IsWindows() && source.TrimEnd(':').Length == 1 && char.IsLetter(source[0]))
                return $@"\\.\{char.ToUpperInvariant(source[0])}:";

            return source;
        }
    }
}
