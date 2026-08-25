using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Worker.Configuration;

public sealed class WorkerOptions
{
    public const string SectionName = AppConst.Configuration.RootSection;

    public bool Enabled { get; set; } = AppConst.Defaults.WorkerEnabled;

    public string WorkerId { get; set; } = AppConst.Defaults.WorkerId;
}
