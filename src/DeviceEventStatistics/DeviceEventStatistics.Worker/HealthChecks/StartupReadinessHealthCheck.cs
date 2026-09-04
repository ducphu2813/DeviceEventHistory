using DeviceEventStatistics.Domain.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using DeviceEventStatistics.Worker.Configuration;

namespace DeviceEventStatistics.Worker.HealthChecks;

public sealed class StartupReadinessHealthCheck(StartupReadinessState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (state.IsReady)
        {
            return Task.FromResult(
                HealthCheckResult.Healthy(
                    state.IsDisabled
                        ? StatisticsContractConstants.Messages.MSG_HEALTH_DISABLED
                        : StatisticsContractConstants.Messages.MSG_HEALTH_READY));
        }

        return Task.FromResult(
            HealthCheckResult.Unhealthy(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_HEALTH_NOT_READY,
                    state.FailureCode ?? StatisticsContractConstants.StartupErrors.NotCompleted)));
    }
}
