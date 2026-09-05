using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Attributes
{
    public enum NtfsAttributeType : uint
    {
        StandardInformation = 0x10,
        AttributeList = 0x20,
        FileName = 0x30,
        ObjectId = 0x40,
        SecurityDescriptor = 0x50,
        VolumeName = 0x60,
        VolumeInfo = 0x70,
        Data = 0x80,
        IndexRoot = 0x90,
        IndexAllocation = 0xA0,
        Bitmap = 0xB0,
        Reparse = 0xC0,
        EaInfo = 0xD0,
        Ea = 0xE0,
        PropertySet = 0xF0,
        LoggedUtilityStream = 0x100,
        EndMarker = 0xFFFFFFFF
    }
}
