namespace Nternal.Partitioning;

/// <summary>
/// Represents the bounds of a generic partition entry.
/// </summary>
public interface IPartitionEntry
{
    /// <summary>
    /// Represents the first (inclusive) Lba.
    /// </summary>
    ulong StartingLba { get; }
    /// <summary>
    /// Represents the last (inclusive) Lba.
    /// </summary>
    ulong InclusiveEndingLba { get; }
}
