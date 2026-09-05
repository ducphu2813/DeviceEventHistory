using DeviceEventStatistics.Application.Projection;

namespace DeviceEventStatistics.Worker.Configuration;

public static class ProjectionExecutionOptionsFactory
{
    public static IncrementalProjectionOptions Create(
        ProjectionOptions settings,
        ResolvedProjectionDefinition definition)
    {
        return new IncrementalProjectionOptions(
            definition.Identity,
            definition.MappingVersion,
            definition.MetricSetVersion,
            definition.CoverageStartAtUtc,
            settings.BatchSize,
            settings.MaxContributionsPerBatch,
            settings.OverlapWindow,
            settings.ReadSafetyDelay,
            settings.Scope.CompanyIds,
            settings.Scope.DeviceIds);
    }
}
