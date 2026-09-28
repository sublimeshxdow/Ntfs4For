using Nternal.Ntfs.Attributes;

namespace Nternal;

public partial class NtfsVolume
{
    private string? _volumeLabel;
    private string? _volumeVersion;
    private VolumeFlags? _volumeFlags;
    
    public string VolumeLabel
    {
        get
        {
            EnsureVolumeInformationIsLoaded();
            return _volumeLabel!;
        }
    }

    public string VolumeVersion
    {
        get
        {
            EnsureVolumeInformationIsLoaded();
            return _volumeVersion!;
        }
    }

    public VolumeFlags VolumeFlags
    {
        get
        {
            EnsureVolumeInformationIsLoaded();
            return _volumeFlags!.Value;
        }
    }

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