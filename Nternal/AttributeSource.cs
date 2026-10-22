using Nternal.Ntfs.Generic;

namespace Nternal.Ntfs;

public readonly record struct AttributeSource(FileReference Record, ushort Id);