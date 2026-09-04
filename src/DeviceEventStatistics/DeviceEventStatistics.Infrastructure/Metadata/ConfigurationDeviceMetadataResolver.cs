using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Metadata;

namespace DeviceEventStatistics.Infrastructure.Metadata;

public sealed class ConfigurationDeviceMetadataResolver(
    string timeZoneId,
    int utcOffsetMinutes) : IDeviceMetadataResolver
{
    public DeviceMetadata? Resolve(HistoryEvent historyEvent)
    {
        if (historyEvent.CompanyId is not > 0 || historyEvent.DeviceId is not > 0)
        {
            return null;
        }

        return new DeviceMetadata(
            historyEvent.CompanyId.Value,
            historyEvent.DeviceId.Value,
            timeZoneId,
            utcOffsetMinutes,
            historyEvent.DeviceType,
            historyEvent.DeviceCode,
            historyEvent.DeviceName,
            historyEvent.GateId,
            historyEvent.GateCode,
            historyEvent.GateName,
            "event_payload_candidate");
    }
}
