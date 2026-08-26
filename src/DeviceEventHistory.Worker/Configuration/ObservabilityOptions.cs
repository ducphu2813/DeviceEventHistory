using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Worker.Configuration;

public sealed class ObservabilityOptions
{
    public const string SectionName = AppConst.Configuration.ObservabilitySection;

    public int MongoFailureUnhealthyThreshold { get; set; } =
        AppConst.Defaults.MongoFailureUnhealthyThreshold;

    public int SourceFailureUnhealthyThreshold { get; set; } =
        AppConst.Defaults.SourceFailureUnhealthyThreshold;

    public TimeSpan ProgressStaleAfter { get; set; } =
        TimeSpan.FromMinutes(AppConst.Defaults.ProgressStaleMinutes);
}
