namespace DeviceEventStatistics.Application.Projection;

public interface IProjectionDefinitionResolver
{
    Task<ResolvedProjectionDefinition> ResolveAsync(
        ProjectionDefinitionResolutionRequest request,
        CancellationToken cancellationToken = default);
}
