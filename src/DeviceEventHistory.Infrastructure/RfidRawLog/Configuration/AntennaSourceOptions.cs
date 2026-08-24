namespace DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;

public sealed class AntennaSourceOptions
{
    public string SourceId { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public int CompanyId { get; set; }

    public string TimeZoneId { get; set; } = "SE Asia Standard Time";

    public string FilePattern { get; set; } = "File_*.txt";

    public bool Enabled { get; set; } = true;
}
