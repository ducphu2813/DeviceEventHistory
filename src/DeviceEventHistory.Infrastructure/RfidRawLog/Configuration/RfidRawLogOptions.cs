using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;

public sealed class RfidRawLogOptions
{
    public const string SectionName = AppConst.Configuration.RawLogSection;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(AppConst.Defaults.PollIntervalSeconds);

    public int ReadBufferBytes { get; set; } = AppConst.Defaults.ReadBufferBytes;

    public int MaxRecordBytes { get; set; } = AppConst.Defaults.MaxRecordBytes;

    public int LookbackDays { get; set; } = AppConst.Defaults.LookbackDays;

    public int MaxConcurrentFiles { get; set; } = AppConst.Defaults.MaxConcurrentFiles;

    public FileStartPositionPolicy StartupExistingFilePolicy { get; set; } = FileStartPositionPolicy.End;

    public FileStartPositionPolicy NewFilePolicy { get; set; } = FileStartPositionPolicy.Beginning;

    public List<AntennaSourceOptions> Sources { get; set; } = [];
}
