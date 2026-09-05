using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Domain.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DeviceEventStatistics.Worker.HealthChecks;

public sealed class OperationalHealthCheck(OperationalHealthState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var dependencyFailure = state.DependencyFailure;
        if (dependencyFailure is not null)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    StatisticsContractConstants.Messages.MSG_HEALTH_OPERATIONAL_DEPENDENCY_FAILED,
                    dependencyFailure));
        }

        var evaluation = state.Evaluation;
        if (evaluation is null)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    StatisticsContractConstants.Messages.MSG_HEALTH_OPERATIONAL_NOT_EVALUATED));
        }

        var description = StatisticsContractConstants.Messages.Format(
            StatisticsContractConstants.Messages.MSG_HEALTH_OPERATIONAL_STATUS,
            evaluation.Reason);
        return Task.FromResult(evaluation.Status switch
        {
            StatisticsHealthStatus.Healthy =>
                HealthCheckResult.Healthy(description),
            StatisticsHealthStatus.Degraded =>
                HealthCheckResult.Degraded(description),
            _ => HealthCheckResult.Unhealthy(description)
        });
    }
}
