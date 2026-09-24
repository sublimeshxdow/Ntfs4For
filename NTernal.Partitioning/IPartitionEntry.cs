namespace Nternal.Partitioning;

public interface IPartitionEntry
{
    UInt64 StartingLba { get; }
    UInt64 InclusiveEndingLba { get; }
}
