using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;

public sealed class AntennaSourceOptions
{
    public string SourceId { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public int CompanyId { get; set; }

    public string TimeZoneId { get; set; } = AppConst.RawLog.DefaultTimeZoneId;

    public string FilePattern { get; set; } = AppConst.RawLog.DefaultFilePattern;

    public bool Enabled { get; set; } = true;
}
