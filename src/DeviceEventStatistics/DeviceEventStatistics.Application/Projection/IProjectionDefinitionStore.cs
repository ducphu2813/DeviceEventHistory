namespace DeviceEventStatistics.Application.Projection;

public interface IProjectionDefinitionStore
{
    Task<ResolvedProjectionDefinition?> ResolveOrCreateAsync(
        ProjectionDefinitionResolutionRequest request,
        string lifecycleStatus,
        CancellationToken cancellationToken = default);
}
