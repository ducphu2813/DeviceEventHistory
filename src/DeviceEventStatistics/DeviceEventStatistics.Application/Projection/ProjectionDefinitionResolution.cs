namespace DeviceEventStatistics.Application.Projection;

public sealed record ProjectionDefinitionResolutionRequest(
    ProjectionIdentity Identity,
    string MappingVersion,
    string OwnershipVersion,
    int MetricSetVersion,
    DateTimeOffset? CoverageStartAtUtc,
    string TimeZoneId,
    bool ResumeFromStoredDefinition,
    bool RequiresBuildLifecycle);

public sealed record ResolvedProjectionDefinition(
    ProjectionIdentity Identity,
    string MappingVersion,
    string OwnershipVersion,
    int MetricSetVersion,
    DateTimeOffset CoverageStartAtUtc,
    string TimeZoneId,
    string LifecycleStatus,
    bool IsNew)
{
    public bool IsUsableByContinuousProjection =>
        LifecycleStatus is ProjectionLifecycleStatuses.Ready or ProjectionLifecycleStatuses.Active;
}
