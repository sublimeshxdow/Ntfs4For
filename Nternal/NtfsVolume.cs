using Nternal.Streams;
using Nternal.Core.Streams;
using System.Diagnostics;

namespace Nternal
{
    public partial class NtfsVolume
    {
        private IDataStream _provider;

        public NtfsBootSector BootSector { get; private set; }

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
        }
    }
}
