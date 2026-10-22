namespace Nternal.Ntfs;

[Flags]
public enum ClusterState : byte
{
    Free = 0x00,
    Allocated = 0x01,
    Bad = 0x02
}