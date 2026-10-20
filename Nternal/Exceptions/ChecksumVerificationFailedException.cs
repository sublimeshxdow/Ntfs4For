namespace Nternal.Ntfs.Exceptions;

public class ChecksumVerificationFailedException(string message = "Failed to verify checksum!") : Exception(message);