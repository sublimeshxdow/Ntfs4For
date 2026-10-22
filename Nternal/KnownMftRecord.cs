namespace Nternal.Ntfs;

public enum KnownMftRecord : ulong
{
    Mft = 0,
    MftMirr = 1,
    LogFile = 2,
    Volume = 3,
    AttrDef = 4,
    Root = 5,
    Bitmap = 6,
    Boot = 7,
    BadClus = 8,
    Secure = 9,
    UpCase = 10,
    Extend = 11
}