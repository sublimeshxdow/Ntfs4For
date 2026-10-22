using Nternal.Partitioning;

namespace PartitionViewer;

internal sealed record Partition(string Number, ulong StartingLba, ulong InclusiveEndingLba, string Type)
    : IPartitionEntry
{
    public string? Name { get; init; }
    public string Flags { get; init; } = "";
    public bool IsContainer { get; init; } // MBR extended partition: holds logical partitions
    public bool IsLogical { get; init; } // MBR logical partition inside the extended one
    public string? FileSystem { get; set; }
    public string? VolumeLabel { get; set; }
    public List<string> Details { get; } = []; // shown with --verbose

    public ulong Sectors => InclusiveEndingLba - StartingLba + 1;
}