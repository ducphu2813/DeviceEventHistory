using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.HealthChecks;

public sealed class SourcePathHealthCheck(
    IOptions<RfidRawLogOptions> rawLogOptions,
    IngestionHealthState healthState,
    IOptions<Configuration.WorkerOptions> workerOptions) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!workerOptions.Value.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                AppConst.Observability.HealthWorkerDisabledDescription));
        }

        var sources = rawLogOptions.Value.Sources.Where(source => source.Enabled).ToArray();
        var validRoots = sources.Count(IsConfiguredSource);
        var state = healthState.Snapshot;

        if (validRoots == 0)
        {
            return Task.FromResult(new HealthCheckResult(
                HealthStatus.Unhealthy,
                AppConst.Observability.HealthNoSourceDescription));
        }

        if (state.StartupReady && state.ConfiguredSourceCount > 0 && state.AvailableSourceCount == 0)
        {
            return Task.FromResult(new HealthCheckResult(
                HealthStatus.Unhealthy,
                AppConst.Observability.HealthNoReadableSourceDescription));
        }

        if (validRoots < sources.Length || state.Status == IngestionHealthStatus.Degraded)
        {
            return Task.FromResult(new HealthCheckResult(
                HealthStatus.Degraded,
                AppConst.Observability.HealthSourceAttentionDescription));
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }

    private static bool IsConfiguredSource(AntennaSourceOptions source) =>
        source.Mode switch
        {
            RawLogSourceMode.Local => Directory.Exists(source.RootPath),
            RawLogSourceMode.RemoteHttp => Uri.TryCreate(
                source.RemoteBaseUrl,
                UriKind.Absolute,
                out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
            _ => false
        };
}
