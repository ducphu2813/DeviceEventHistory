using DeviceEventStatistics.Application.History;

namespace DeviceEventStatistics.Application.Metadata;

public sealed record DeviceMetadata(
    long CompanyId,
    long DeviceId,
    string TimeZoneId,
    int UtcOffsetMinutes,
    string? DeviceType,
    string? DeviceCode,
    string? DeviceName,
    long? GateId,
    string? GateCode,
    string? GateName,
    string Source);

public interface IDeviceMetadataResolver
{
    DeviceMetadata? Resolve(HistoryEvent historyEvent);
}
