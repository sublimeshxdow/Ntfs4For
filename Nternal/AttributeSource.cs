using Nternal.Structs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Ntfs
{
    public readonly record struct AttributeSource(FileReference Record, ushort Id);
}
