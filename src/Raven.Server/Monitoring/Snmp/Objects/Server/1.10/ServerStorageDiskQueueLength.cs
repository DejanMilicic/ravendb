using Lextm.SharpSnmpLib;
using Raven.Server.ServerWide;

namespace Raven.Server.Monitoring.Snmp.Objects.Database;

public class ServerStorageDiskQueueLength : ScalarObjectBase<Gauge32>
{
    private readonly ServerStore _store;
    private static readonly Gauge32 Empty = new Gauge32(-1);

    public ServerStorageDiskQueueLength(ServerStore store)
        : base(SnmpOids.Server.StorageDiskQueueLength)
    {
        _store = store;
    }
        
    protected override Gauge32 GetData()
    {
        if (_store.Configuration.Core.RunInMemory)
            return Empty;

        var result = _store.Server.DiskStatsGetter.Get(_store._env.Options.DriveInfoByPath?.Value.BasePath.DriveName);
        return result == null || result.QueueLength.HasValue == false ? Empty  : new Gauge32(result.QueueLength.Value);
    }
}
