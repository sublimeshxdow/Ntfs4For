using Nternal.Streams;
using Nternal.Core.Streams;
using System.Diagnostics;
using Nternal.Attributes;
using Nternal.Helpers;

namespace Nternal
{
    public partial class NtfsVolume
    {
        private IDataStream _provider;

        public NtfsBootSector BootSector { get; private set; }
        public FixupHelper FixupHelper { get; private set; }

        public NtfsVolume(IDataStream dataProvider)
        {
            _provider = dataProvider;
        }

        public void Mount()
        {
            ParseBootSector();
        }

        private void ParseBootSector()
        {
            Span<byte> bootSector = stackalloc byte[NtfsBootSector.BootSectorSize];
            _provider.ReadExactly(0, bootSector);
            
            BootSector = new(bootSector);
            FixupHelper = new(BootSector.BytesPerSector);
        }

        internal IDataStream GetUnderlyingStream() => _provider;
        
        public NtfsFile Mft { get; private set; }
    }
}
