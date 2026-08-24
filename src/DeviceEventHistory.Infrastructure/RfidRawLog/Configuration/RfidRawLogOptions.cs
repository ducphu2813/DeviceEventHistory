namespace DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;

public sealed class RfidRawLogOptions
{
    public const string SectionName = "DeviceEventHistory:RawLog";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int ReadBufferBytes { get; set; } = 512 * 1024;

    public int MaxRecordBytes { get; set; } = 1024 * 1024;

    public int LookbackDays { get; set; } = 1;

    public int MaxConcurrentFiles { get; set; } = 4;

    public FileStartPositionPolicy StartupExistingFilePolicy { get; set; } = FileStartPositionPolicy.End;

    public FileStartPositionPolicy NewFilePolicy { get; set; } = FileStartPositionPolicy.Beginning;

    public List<AntennaSourceOptions> Sources { get; set; } = [];
}
