using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Partitioning.Gpt
{
    public struct GptPartitionEntry
    {
        public Guid TypeGuid { get; init; }
        public Guid PartitionGuid { get; init; }
        public ulong StartingLba { get; init; }
        public ulong EndingLba { get; init; }
        public GptPartitionFlag Flags { get; init; }
        public string Name { get; init; }
    }
}
