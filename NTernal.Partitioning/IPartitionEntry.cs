namespace Nternal.Partitioning;

public interface IPartitionEntry
{
    ulong StartingLba { get; }
    ulong InclusiveEndingLba { get; }
}
