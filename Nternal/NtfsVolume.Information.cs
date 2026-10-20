using Nternal.Ntfs.Attributes;

namespace Nternal;

public partial class NtfsVolume
{
    private string? _volumeLabel;
    private string? _volumeVersion;
    private VolumeFlags? _volumeFlags;
    
    /// <summary>
    /// Represents the volume label of this volume.
    /// </summary>
    public string VolumeLabel
    {
        get
        {
            EnsureVolumeInformationIsLoaded();
            return _volumeLabel!;
        }
    }
    
    /// <summary>
    /// Represents the Ntfs version of the underlying file system.
    /// </summary>
    public string NtfsVersion
    {
        get
        {
            EnsureVolumeInformationIsLoaded();
            return _volumeVersion!;
        }
    }
    
    /// <summary>
    /// Represents the currently set volume flags.
    /// </summary>
    public VolumeFlags VolumeFlags
    {
        get
        {
            EnsureVolumeInformationIsLoaded();
            return _volumeFlags!.Value;
        }
    }
    
    /// <summary>
    /// Loads relevant members for the fields above.
    /// </summary>
    private void EnsureVolumeInformationIsLoaded()
    {
        EnsureIsMounted();

        if (_volumeLabel == null || _volumeVersion == null || _volumeFlags == null)
        {
            var nameAttr = VolumeFile!.GetAttributeData<VolumeNameAttribute>();
            var infoAttr = VolumeFile!.GetAttributeData<VolumeInformationAttribute>();
            
            _volumeLabel = nameAttr!.VolumeName;
            _volumeVersion = infoAttr!.NtfsVersion;
            _volumeFlags = infoAttr!.Flags;
        }
    }
}