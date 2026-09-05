using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Application.Projection;

public sealed class ProjectionDefinitionResolver(
    IProjectionDefinitionStore store)
    : IProjectionDefinitionResolver
{
    public async Task<ResolvedProjectionDefinition> ResolveAsync(
        ProjectionDefinitionResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        if (!request.ResumeFromStoredDefinition && request.CoverageStartAtUtc is null)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_PROJECTION_COVERAGE_START_MISSING);
        }

        var lifecycleStatus = request.RequiresBuildLifecycle
            ? ProjectionLifecycleStatuses.Building
            : ProjectionLifecycleStatuses.Active;
        var stored = await store.ResolveOrCreateAsync(
            request,
            lifecycleStatus,
            cancellationToken);
        if (stored is null)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_RECOVERY_DEFINITION_MISSING,
                    request.Identity.ProjectionName,
                    request.Identity.ProjectionVersion));
        }

        ValidateImmutableContract(request, stored);
        if (request.RequiresBuildLifecycle)
        {
            return stored;
        }

        if (!stored.IsUsableByContinuousProjection)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_PROJECTION_DEFINITION_NOT_USABLE,
                    request.Identity.ProjectionName,
                    request.Identity.ProjectionVersion,
                    stored.LifecycleStatus));
        }

        return stored;
    }

    private static void ValidateRequest(ProjectionDefinitionResolutionRequest request)
    {
        if (request.Identity.ProjectionVersion <= 0 ||
            string.IsNullOrWhiteSpace(request.Identity.ProjectionName) ||
            string.IsNullOrWhiteSpace(request.MappingVersion) ||
            string.IsNullOrWhiteSpace(request.OwnershipVersion) ||
            request.MetricSetVersion <= 0 ||
            string.IsNullOrWhiteSpace(request.TimeZoneId))
        {
            throw new ArgumentException(
                StatisticsContractConstants.Messages.MSG_PROJECTION_DEFINITION_REQUEST_INVALID);
        }
    }

    private static void ValidateImmutableContract(
        ProjectionDefinitionResolutionRequest request,
        ResolvedProjectionDefinition stored)
    {
        var coverageMismatch = request.CoverageStartAtUtc is DateTimeOffset configuredCoverage &&
            configuredCoverage.ToUniversalTime() != stored.CoverageStartAtUtc.ToUniversalTime();
        if (!string.Equals(stored.MappingVersion, request.MappingVersion, StringComparison.Ordinal) ||
            !string.Equals(stored.OwnershipVersion, request.OwnershipVersion, StringComparison.Ordinal) ||
            stored.MetricSetVersion != request.MetricSetVersion ||
            !string.Equals(stored.TimeZoneId, request.TimeZoneId, StringComparison.Ordinal) ||
            coverageMismatch)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_RECOVERY_DEFINITION_CONFLICT,
                    request.Identity.ProjectionName,
                    request.Identity.ProjectionVersion));
        }
    }
}
